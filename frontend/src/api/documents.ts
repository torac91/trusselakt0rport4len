import { api, ApiError } from './client'
import type { CaseDocument } from '../types/document'

const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5080'

export const documentsApi = {
  list: (caseId: number) => api.get<CaseDocument[]>(`/api/v2/cases/${caseId}/documents`),

  async upload(caseId: number, file: File): Promise<CaseDocument> {
    const form = new FormData()
    form.append('file', file)

    const res = await fetch(`${API_BASE_URL}/api/v2/cases/${caseId}/documents`, {
      method: 'POST',
      credentials: 'include',
      body: form,
    })

    if (!res.ok) {
      let message = `Opplasting feilet (${res.status})`
      try {
        const body = await res.json()
        if (body?.message) message = body.message
      } catch {
        // ignore non-JSON error bodies
      }
      throw new ApiError(res.status, message)
    }

    return (await res.json()) as CaseDocument
  },

  importFromUrl: (caseId: number, url: string) =>
    api.post<CaseDocument>(`/api/v2/cases/${caseId}/documents/import-url`, { url }),

  async uploadZip(caseId: number, file: File): Promise<CaseDocument[]> {
    const form = new FormData()
    form.append('file', file)

    const res = await fetch(`${API_BASE_URL}/api/v2/cases/${caseId}/documents/upload-zip`, {
      method: 'POST',
      credentials: 'include',
      body: form,
    })

    if (!res.ok) {
      let message = `Samleopplasting feilet (${res.status})`
      try {
        const body = await res.json()
        if (body?.message) message = body.message
      } catch {
        // ignore non-JSON error bodies
      }
      throw new ApiError(res.status, message)
    }

    return (await res.json()) as CaseDocument[]
  },

  downloadUrl: (id: number) => `${API_BASE_URL}/api/documents/${id}`,
  previewUrl: (id: number) => `${API_BASE_URL}/api/documents/${id}/preview`,
}
