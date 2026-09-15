import json, urllib.request
url = "http://127.0.0.1:5088/api/nm-documents/b515238e-c487-49a0-947d-775033b8832e/review-rows"
data = json.load(urllib.request.urlopen(url))
rows = data["rows"]
print({"rows": len(rows), "fragments": len(data["fragments"]), "unresolved_khasras": sum(1 for row in rows for k in row["khasras"] if k["suggestedKhasraId"] is None), "verified": sum(row["status"] == "Verified" for row in rows)})
