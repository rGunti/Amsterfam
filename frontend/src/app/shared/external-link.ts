/**
 * Whether a click on a link written by somebody else should ask before leaving the app.
 * Anything that opens the link somewhere else (middle-click, Ctrl/Cmd/Shift/Alt-click) is
 * already a deliberate choice and is left to the browser.
 */
export function isPlainLeftClick(event: MouseEvent): boolean {
  return !(event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey);
}
