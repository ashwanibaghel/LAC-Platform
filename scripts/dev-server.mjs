import { spawn } from 'node:child_process';
import { existsSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const webRoot = path.join(root, 'src', 'LAC.Web');
const apiProject = path.join(root, 'src', 'LAC.Api', 'LAC.Api.csproj');
const apiDll = path.join(root, 'src', 'LAC.Api', 'bin', 'Release', 'net10.0', 'LAC.Api.dll');
const viteScript = path.join(webRoot, 'node_modules', 'vite', 'bin', 'vite.js');
const apiUrl = 'http://127.0.0.1:5088';
const apiBindHost = process.env.LAC_API_BIND_HOST ||
  (process.env.OnlyOffice__Enabled?.toLowerCase() === 'true' ? '0.0.0.0' : '127.0.0.1');
const args = process.argv.slice(2);
const apiOnly = args.includes('--api-only');
const viteArgs = args.filter(arg => arg !== '--api-only');
const portIndex = viteArgs.indexOf('--port');
const webPort = portIndex >= 0 ? Number(viteArgs[portIndex + 1]) : 5173;

let apiProcess;
let webProcess;
let stopping = false;
let checking = false;
let failedHealthChecks = 0;
let reportedExistingWeb = false;

const delay = ms => new Promise(resolve => setTimeout(resolve, ms));

async function status(url) {
  try {
    const response = await fetch(url, { signal: AbortSignal.timeout(2500) });
    return response.status;
  } catch {
    return null;
  }
}

async function apiStatus() {
  const code = await status(`${apiUrl}/api/health`);
  if (code !== null && code !== 200 && code !== 503) {
    throw new Error(`Port 5088 answered /api/health with HTTP ${code}; check what is using that port.`);
  }
  return code;
}

function run(command, commandArgs, cwd) {
  return new Promise((resolve, reject) => {
    const child = spawn(command, commandArgs, { cwd, stdio: 'inherit', windowsHide: true });
    child.once('error', reject);
    child.once('exit', code => code === 0 ? resolve() : reject(new Error(`${command} exited with code ${code}`)));
  });
}

async function ensureApi() {
  if (await apiStatus() !== null) {
    failedHealthChecks = 0;
    return;
  }
  if (apiProcess) {
    failedHealthChecks++;
    if (failedHealthChecks < 3) return;
    console.error('[LAC dev] API stopped responding; restarting it.');
    apiProcess.kill();
    apiProcess = undefined;
  }

  failedHealthChecks = 0;
  console.log('[LAC dev] API is offline; building and starting port 5088.');
  await run('dotnet', ['build', apiProject, '-c', 'Release', '--nologo', '--verbosity', 'minimal'], root);
  if (!existsSync(apiDll)) throw new Error('API build succeeded but LAC.Api.dll is missing.');
  if (await apiStatus() !== null) return;

  apiProcess = spawn('dotnet', [apiDll, '--urls', `http://${apiBindHost}:5088`], {
    cwd: path.dirname(apiProject),
    stdio: 'inherit',
    windowsHide: true,
    env: {
      ...process.env,
      'Logging__LogLevel__Microsoft.EntityFrameworkCore': 'Warning',
      'Logging__LogLevel__Microsoft.AspNetCore': 'Warning',
    },
  });
  apiProcess.once('error', error => {
    console.error(`[LAC dev] API could not start: ${error.message}`);
    apiProcess = undefined;
  });
  apiProcess.once('exit', code => {
    if (!stopping) console.error(`[LAC dev] API exited (${code}); retrying automatically.`);
    apiProcess = undefined;
  });

  for (let attempt = 0; attempt < 45 && !stopping; attempt++) {
    if (await apiStatus() !== null) {
      console.log(`[LAC dev] API ready: ${apiUrl}/api/health`);
      return;
    }
    if (!apiProcess) throw new Error('API exited before becoming ready.');
    await delay(2000);
  }
  throw new Error('API did not become ready within 90 seconds.');
}

async function ensureWeb() {
  if (apiOnly || webProcess) return;
  if (await status(`http://127.0.0.1:${webPort}/@vite/client`) === 200) {
    if (!reportedExistingWeb) console.log(`[LAC dev] Web already running on port ${webPort}.`);
    reportedExistingWeb = true;
    return;
  }
  if (await status(`http://127.0.0.1:${webPort}/`) !== null) {
    throw new Error(`Port ${webPort} is occupied by a server that is not Vite.`);
  }
  reportedExistingWeb = false;
  if (!existsSync(viteScript)) throw new Error(`Vite is missing. Run npm ci in ${webRoot}.`);
  webProcess = spawn(process.execPath, [viteScript, ...viteArgs, '--strictPort'], {
    cwd: webRoot,
    stdio: 'inherit',
    windowsHide: true,
  });
  webProcess.once('error', error => {
    console.error(`[LAC dev] Web could not start: ${error.message}`);
    webProcess = undefined;
  });
  webProcess.once('exit', code => {
    if (!stopping) console.error(`[LAC dev] Web exited (${code}); retrying automatically.`);
    webProcess = undefined;
  });
}

function stop() {
  if (stopping) return;
  stopping = true;
  apiProcess?.kill();
  webProcess?.kill();
  process.exit(0);
}

process.on('SIGINT', stop);
process.on('SIGTERM', stop);

while (!stopping) {
  try {
    await ensureApi();
    await ensureWeb();
    break;
  } catch (error) {
    console.error(`[LAC dev] ${error.message}`);
    await delay(10000);
  }
}

setInterval(async () => {
  if (stopping || checking) return;
  checking = true;
  try {
    await ensureApi();
    await ensureWeb();
  } catch (error) {
    console.error(`[LAC dev] ${error.message}`);
  } finally {
    checking = false;
  }
}, 5000);
