/**
 * Where a form's Cancel button and its successful save go back to.
 *
 * A list screen puts its own URL — filters, page and sort included — into history state when it links to a
 * form, so saving returns the user to the view they left rather than to an unfiltered page one.
 *
 * History state can hold whatever a link chose to put there, so the value is only honoured when it
 * addresses the list it claims to: the module's own base path, optionally with a query string. Anything
 * else falls back to the plain list path. That is the same posture `LoginPage.redirectTarget` takes with
 * the `from` a route guard hands it — a value that came from outside is checked before it is navigated to.
 */
export function returnPath(state: unknown, basePath: string): string {
  const from = (state as { from?: unknown } | null)?.from
  if (typeof from !== 'string') return basePath
  return from === basePath || from.startsWith(`${basePath}?`) ? from : basePath
}
