export interface CreateAwardInput {
  awardNumber: string;
  awardDate?: string | null;
  awardType?: string | null;
}

export interface CreateAwardOrchestrationOptions {
  api: string;
  villageId: string;
  awardData: CreateAwardInput;
  initialFile: File | null;
  fetchFn?: typeof fetch;
}

export interface CreateAwardOrchestrationResult {
  awardCreated: boolean;
  awardId?: string;
  pdfUploaded: boolean;
  error?: string;
  message?: string;
}

export async function createAwardWithOptionalPdf({
  api,
  villageId,
  awardData,
  initialFile,
  fetchFn = fetch,
}: CreateAwardOrchestrationOptions): Promise<CreateAwardOrchestrationResult> {
  // Step 1: Create Award entry
  const res = await fetchFn(`${api}/villages/${villageId}/awards`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({
      awardNumber: awardData.awardNumber.trim(),
      awardDate: awardData.awardDate || null,
      awardType: awardData.awardType || "General",
    }),
    credentials: "include",
  });

  if (!res.ok) {
    const err = await res.json().catch(() => ({}));
    const errorMsg = err.detail || err.message || "Could not add award.";
    return {
      awardCreated: false,
      pdfUploaded: false,
      error: errorMsg,
    };
  }

  const data = await res.json();
  const createdAwardId = data.id as string;

  // Step 2: Optional initial PDF upload
  if (initialFile && createdAwardId) {
    try {
      const formData = new FormData();
      formData.append("file", initialFile);
      const uploadRes = await fetchFn(`${api}/awards/${createdAwardId}/core-documents?role=Award`, {
        method: "POST",
        body: formData,
        credentials: "include",
      });

      if (!uploadRes.ok) {
        const err = await uploadRes.json().catch(() => ({}));
        const uploadErrDetail = err.detail || err.message;
        return {
          awardCreated: true,
          awardId: createdAwardId,
          pdfUploaded: false,
          message: uploadErrDetail
            ? `Award created successfully, but the PDF could not be attached (${uploadErrDetail}). Add the Award PDF from Core Records.`
            : "Award created successfully, but the PDF could not be attached. Add the Award PDF from Core Records.",
        };
      }
    } catch (err: any) {
      return {
        awardCreated: true,
        awardId: createdAwardId,
        pdfUploaded: false,
        message: "Award created successfully, but the PDF could not be attached. Add the Award PDF from Core Records.",
      };
    }
  }

  return {
    awardCreated: true,
    awardId: createdAwardId,
    pdfUploaded: Boolean(initialFile),
  };
}
