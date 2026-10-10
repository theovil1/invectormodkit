// Messages with the C# side (WebView2): call(method, params) -> Promise of the result; on(event, fn) for what the
// host sends by itself (closing the window).

const host = window.chrome && window.chrome.webview;
const pending = new Map();
const listeners = {};
let next = 1;

export const inHost = !!host;

if (host)
  host.addEventListener("message", (e) => {
    const m = e.data;
    if (m && m.id && pending.has(m.id)) {
      const { resolve, reject } = pending.get(m.id);
      pending.delete(m.id);
      if (m.error) reject(new Error(m.error));
      else resolve(m.result);
    } else if (m && m.event) for (const fn of listeners[m.event] || []) fn(m.data);
  });

export function call(method, params = {}) {
  if (!host) return Promise.reject(new Error("This page runs inside InvectorChartEditor.exe."));
  return new Promise((resolve, reject) => {
    const id = next++;
    pending.set(id, { resolve, reject });
    host.postMessage({ id, method, params });
  });
}

export function on(event, fn) {
  (listeners[event] = listeners[event] || []).push(fn);
}
