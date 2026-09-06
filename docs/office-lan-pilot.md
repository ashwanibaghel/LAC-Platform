# Office LAN pilot deployment

This is a practical pilot topology, not a final enterprise production architecture.

```text
Office browsers ── HTTP/HTTPS LAN ── IIS + LAC API/React host PC
                                      ├─ PostgreSQL (127.0.0.1 only)
                                      ├─ local SSD / approved file storage documents
                                      └─ one local PDF extraction worker
```

The host PC runs Windows, IIS, the .NET API, the compiled React application, PostgreSQL on localhost, and the configured document/extraction/backup roots. Other office PCs need only a browser and access the same-origin application URL, for example `https://lac-office/` with API calls under `/api/*`.

Internet and Supabase are not required. Do not expose PostgreSQL port 5432 to office LAN clients; only IIS/API should access it. Documents are kept on host storage and should be served by authenticated/authorized API endpoints when document viewing is added.

Configure secrets as host-level environment variables or IIS configuration protected outside source control. Create a dedicated `lac_app` PostgreSQL role, use a local DB backup plus a filesystem copy of documents, and monitor `/api/health` after deployment. Keep extraction concurrency at one for the pilot PC unless hardware capacity has been assessed.
