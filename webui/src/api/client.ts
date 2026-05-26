const BASE = '/api'

export async function loadRepository(path: string, forceReload = false) {
  const res = await fetch(`${BASE}/load`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ path, forceReload }) })
  return res.json()
}
export async function getLoadStatus() {
  const res = await fetch(`${BASE}/load/status`)
  return res.json()
}
export async function reloadRepository() {
  const res = await fetch(`${BASE}/reload`, { method: 'POST' })
  return res.json()
}
export async function getSession() {
  const res = await fetch(`${BASE}/session`)
  return res.json()
}
export async function getHistory() {
  const res = await fetch(`${BASE}/history`)
  return res.json()
}
export async function deleteHistory(path: string) {
  const res = await fetch(`${BASE}/history`, { method: 'DELETE', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ path }) })
  return res.json()
}
export async function getEvidence(nodeId: string) {
  const res = await fetch(`${BASE}/evidence/${nodeId}`)
  return res.json()
}

// Unified agent — single endpoint for cognition queries
export async function agent(message: string) {
  const res = await fetch(`${BASE}/agent`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ message }) })
  return res.json()
}
