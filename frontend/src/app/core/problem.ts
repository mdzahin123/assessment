import { HttpErrorResponse } from '@angular/common/http';

/**
 * Turns an API error into one readable sentence.
 * The API always answers errors with ProblemDetails JSON: { title, detail } or, for invalid input,
 * { errors: { field: [messages] } }.
 */
export function problemMessage(error: unknown): string {
  if (error instanceof HttpErrorResponse) {
    if (error.status === 0) return 'Cannot reach the server. Is the API running?';

    const body = error.error as { title?: string; detail?: string; errors?: Record<string, string[]> } | null;
    if (body?.errors) return Object.values(body.errors).flat().join(' ');
    if (body?.detail) return body.detail;
    if (body?.title) return body.title;
    return `Request failed (${error.status}).`;
  }
  return 'Something went wrong.';
}
