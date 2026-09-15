import json, sys
data = json.load(open(sys.argv[1] if len(sys.argv) > 1 else "tmp/nm-pilot-output.json", encoding="utf-8"))
print({"pages": data["pagesProcessed"], "safe": sum(x["candidateType"] == "NmReviewRow" for x in data["candidates"]), "fragments": sum(x["candidateType"] == "UnassignedSourceFragment" for x in data["candidates"])})
