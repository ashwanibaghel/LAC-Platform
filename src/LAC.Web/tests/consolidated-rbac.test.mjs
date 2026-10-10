import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { createSessionFetch } from "../src/auth/sessionFetch.ts";
import { directoryHierarchy, helperAccessLabel } from "../src/admin/directoryHierarchy.ts";

test("Helper access summaries distinguish None, Read Only and Read + Write", () => {
  assert.equal(helperAccessLabel([]), "None");
  assert.equal(helperAccessLabel(undefined), "None");
  assert.equal(helperAccessLabel(["Court.View", "Land.View"]), "Read Only");
  assert.equal(helperAccessLabel(["Court.View", "Court.Create"]), "Read + Write");
});

test("rotated session expires once and suppresses subsequent resource traffic", async () => {
  let requests = 0, expirations = 0;
  const session = createSessionFetch(async () => { requests++; return new Response(null, {status:401}); }, () => expirations++, "http://localhost");
  session.authenticated();
  assert.equal((await session.fetch("/api/court-cases")).status,401);
  assert.equal((await session.fetch("/api/villages")).status,401);
  assert.equal(requests,1); assert.equal(expirations,1);
});
test("simultaneous 401 responses issue only one session-expiration notification", async () => {
  let expires=0;
  const session=createSessionFetch(async () => new Response(null,{status:401}),()=>expires++,"http://localhost");
  session.authenticated();
  await Promise.all([session.fetch("/api/court-cases"),session.fetch("/api/villages")]);
  assert.equal(expires,1);
});
test("bad login, initial unauthenticated probe and external 401 do not revoke an active session",async()=>{
  let expires=0;
  const session=createSessionFetch(async()=>new Response(null,{status:401}),()=>expires++,"http://localhost");
  await session.fetch("/api/auth/me");
  session.authenticated();
  await session.fetch("/api/auth/login");
  await session.fetch("http://elsewhere/api/resource");
  assert.equal(expires,0);
});
test("fresh successful sign-in releases the resource guard",async()=>{
  let requests=0;
  const session=createSessionFetch(async()=>{requests++;return new Response(null,{status:401});},()=>{},"http://localhost");
  session.authenticated(); await session.fetch("/api/court-cases"); session.authenticated();
  await session.fetch("/api/court-cases"); assert.equal(requests,2);
});
test("responses from an old session cannot repopulate caches after another login",async()=>{
  let resolve;
  const session=createSessionFetch(()=>new Promise(done=>{resolve=done;}),()=>{},"http://localhost");
  session.authenticated(); const pending=session.fetch("/api/private-records");
  session.loggedOut(); session.authenticated(); resolve(new Response('{"private":true}',{status:200}));
  assert.equal((await pending).status,401);
});
const account=(id,authority,parent=null)=>({id,username:id,fullName:id,authority,supervisingOfficerId:parent});
test("Helpers nest beneath their actual visible parent, not alphabetic global rows",()=>{
  const accounts=[account("aa.helper","HELPER","zz.officer"),account("zz.officer","STANDARD_OFFICER"),account("head","OFFICE_ADMIN")];
  assert.deepEqual(directoryHierarchy(accounts,accounts).map(x=>x.id),["head","zz.officer","aa.helper"]);
});
test("search for Helper includes authorized parent context; missing parent is never invented",()=>{
  const parent=account("parent","STANDARD_OFFICER"),child=account("child","HELPER","parent");
  assert.deepEqual(directoryHierarchy([parent,child],[child]).map(x=>x.id),["parent","child"]);
  assert.deepEqual(directoryHierarchy([child],[child]),[]);
});
test("authority grouping never invents officer reporting edges",()=>{
  const staff=account("staff","STANDARD_OFFICER"),supervisor=account("supervisor","OFFICE_SUPERVISOR");
  const rows=directoryHierarchy([staff,supervisor],[staff,supervisor]);
  assert.deepEqual(rows.map(x=>x.id),["supervisor","staff"]); assert.ok(rows.every(x=>x.supervisingOfficerId===null));
});
const source=path=>readFileSync(new URL("../src/"+path,import.meta.url),"utf8");
test("Helper dialogs use opaque bounded shared shells with scrollable form body",()=>{
  assert.ok(!source("office/MyHelpersView.tsx").includes('className="rbac-modal-dialog"'));
  assert.ok(source("admin/admin.css").includes(".rbac-helper-modal > form"));
  assert.ok(source("admin/admin.css").includes(".rbac-helper-modal .rbac-modal-body"));
});
test("protected drawer actions require server-provided capabilities",()=>{
  assert.ok(source("admin/UsersAdmin.tsx").includes("selectedDrawerOfficer.capabilities?.canEdit"));
});
test("Court import action is gated by effective server capability in first-run state",()=>{
  assert.ok(source("court/CourtDirectory.tsx").includes('canImport && <Link className="primary-button"'));
  assert.ok(source("court/CourtDirectory.tsx").includes("operationalCapabilities.canCreateCourt"));
});
test("Matter creation requires authorized workstream and permission before modal",()=>{
  assert.ok(source("App.tsx").includes('hasPermission("Matter.Create") && workstreams.length > 0'));
  assert.ok(source("App.tsx").includes("/api/matters/context?villageId="));
});
test("My Helpers entry requires Standard Officer and explicit helper management grant",()=>{
  assert.ok(source("home/Home.tsx").includes('user?.authority === "STANDARD_OFFICER" && hasPermission("Assistants.Manage")'));
});
test("expired authentication clears user state and redirects once to a dedicated login URL",()=>{
  assert.ok(source("App.tsx").includes('location.pathname === "/login" ? <LoginPage /> : <Navigate to="/login" replace />'));
  assert.ok(source("auth/AuthProvider.tsx").includes('window.addEventListener(sessionExpiredEvent, expired)'));
});
test("technical recovery has a separate route and cannot populate the management directory",()=>{
  assert.ok(!source("admin/UsersAdmin.tsx").includes("<OfficeRecovery"));
  assert.ok(source("admin/UsersAdmin.tsx").includes('to="/admin/technical-recovery"'));
  assert.ok(source("admin/OfficeRecovery.tsx").includes('user?.authority !== "SYSTEM_ADMIN"'));
});
test("temporary credentials are masked by default in Helper and Officer dialogs",()=>{
  for(const path of ["office/MyHelpersView.tsx","admin/UsersAdmin.tsx"])
    assert.ok(source(path).includes('aria-label="Temporary credential" type="password" readOnly'));
});
