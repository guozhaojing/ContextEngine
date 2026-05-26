import { useState, useRef, useEffect } from 'react'
import * as api from './api/client'
import type { RepositoryHistoryEntry } from './types/api'

type Message = {
  role: 'user' | 'system'
  content: string
  type?: 'query' | 'codefix'
  details?: any
}

export default function App() {
  const [session, setSession] = useState<any>(null)
  const [history, setHistory] = useState<RepositoryHistoryEntry[]>([])
  const [loading, setLoading] = useState(false)
  const [loadProgress, setLoadProgress] = useState<any>(null)
  const [path, setPath] = useState('')
  const [input, setInput] = useState('')
  const [messages, setMessages] = useState<Message[]>([])
  const [fromCache, setFromCache] = useState(false)
  const chatRef = useRef<HTMLDivElement>(null)

  useEffect(() => { api.getSession().then(setSession) }, [])
  useEffect(() => { api.getHistory().then(setHistory) }, [])
  useEffect(() => { chatRef.current?.scrollTo(0, chatRef.current.scrollHeight) }, [messages])

  async function pollLoadStatus() {
    const interval = 400
    const maxWait = 300_000 // 5 min timeout
    const start = Date.now()
    while (Date.now() - start < maxWait) {
      await new Promise(r => setTimeout(r, interval))
      const status = await api.getLoadStatus()
      setLoadProgress(status.progress || null)
      if (!status.loading) {
        setLoading(false)
        setLoadProgress(null)
        if (status.complete && status.result?.success) {
          setFromCache(status.result.fromCache ?? false)
          setSession(await api.getSession())
          setHistory(await api.getHistory())
        } else if (status.complete && !status.result?.success) {
          setMessages(prev => [...prev, { role: 'system', content: `加载失败: ${status.result?.error || '未知错误'}` }])
        }
        return
      }
    }
    setLoading(false)
    setLoadProgress(null)
    setMessages(prev => [...prev, { role: 'system', content: '加载超时（超过5分钟）' }])
  }

  async function handleLoad() {
    if (!path.trim()) return
    setLoading(true)
    setLoadProgress(null)
    const r = await api.loadRepository(path.trim())
    if (!r.loading) {
      setMessages(prev => [...prev, { role: 'system', content: `加载失败: ${r.error || '未知错误'}` }])
      setLoading(false)
      return
    }
    pollLoadStatus()
  }

  async function handleLoadFromHistory(hPath: string) {
    setPath(hPath); setLoading(true); setLoadProgress(null)
    const r = await api.loadRepository(hPath)
    if (!r.loading) {
      setMessages(prev => [...prev, { role: 'system', content: `加载失败: ${r.error || '未知错误'}` }])
      setLoading(false)
      return
    }
    pollLoadStatus()
  }

  async function handleReload() {
    setLoading(true)
    setLoadProgress(null)
    await api.reloadRepository()
    pollLoadStatus()
  }

  async function handleDeleteHistory(hPath: string) {
    await api.deleteHistory(hPath); setHistory(await api.getHistory())
  }

  async function handleSend(e: React.FormEvent) {
    e.preventDefault()
    if (!input.trim() || !session?.isLoaded) return
    const msg = input.trim()
    setInput('')
    setMessages(prev => [...prev, { role: 'user', content: msg }])
    setLoading(true)
    try {
      const result = await api.agent(msg)
      setMessages(prev => [...prev, {
        role: 'system',
        content: result.summary || result.title || '',
        type: result.type,
        details: result,
      }])
      setSession(await api.getSession())
    } catch (e: any) {
      setMessages(prev => [...prev, { role: 'system', content: `错误: ${e.message}` }])
    }
    setLoading(false)
  }

  const lastDetails = [...messages].reverse().find(m => m.details)?.details

  return (
    <div className="h-screen flex flex-col">
      <header className="bg-gray-900 border-b border-gray-800 px-4 py-2 flex items-center gap-4 shrink-0">
        <h1 className="text-lg font-bold text-blue-400">ContextEngine</h1>
        <span className="text-gray-600 text-sm">Agent</span>
        <div className="flex-1" />
        {session?.isLoaded && (
          <span className="text-xs text-gray-500">{session.repositoryName} | {session.nodeCount}节点 | {session.totalQueries}次</span>
        )}
      </header>

      <div className="flex-1 flex overflow-hidden">
        {/* Left sidebar */}
        <aside className="w-56 bg-gray-900 border-r border-gray-800 p-3 flex flex-col gap-2 shrink-0 overflow-y-auto">
          <h2 className="text-xs font-semibold text-gray-500 uppercase tracking-wider">仓库</h2>
          {!session?.isLoaded ? (
            <div className="flex flex-col gap-1.5">
              <input value={path} onChange={e => setPath(e.target.value)} onKeyDown={e => e.key === 'Enter' && handleLoad()}
                placeholder="输入路径..." disabled={loading}
                className="bg-gray-800 border border-gray-700 rounded px-2 py-1 text-xs text-gray-200 placeholder-gray-500 focus:outline-none focus:border-blue-500 disabled:opacity-50" />
              <button onClick={handleLoad} disabled={loading}
                className="bg-blue-600 hover:bg-blue-700 disabled:opacity-50 text-white text-xs rounded px-2 py-1 transition">{loading ? '加载中...' : '加载'}</button>
              {loading && loadProgress && (
                <div className="bg-gray-800 rounded p-2 space-y-1.5">
                  <div className="flex justify-between text-[10px]">
                    <span className="text-blue-400">{stageLabel(loadProgress.stage)}</span>
                    <span className="text-gray-500">{Math.round(loadProgress.percent * 100)}%</span>
                  </div>
                  <div className="w-full bg-gray-700 rounded-full h-1.5 overflow-hidden">
                    <div className="bg-blue-500 h-full rounded-full transition-all duration-300" style={{ width: `${Math.round(loadProgress.percent * 100)}%` }} />
                  </div>
                  {loadProgress.totalFiles > 0 && (
                    <div className="text-[10px] text-gray-500">
                      文件 {loadProgress.currentFile}/{loadProgress.totalFiles}
                      {loadProgress.currentFilePath && <span className="block text-gray-600 truncate">{loadProgress.currentFilePath}</span>}
                    </div>
                  )}
                  {loadProgress.totalProjects > 0 && (
                    <div className="text-[10px] text-gray-500">项目 {loadProgress.currentProject}/{loadProgress.totalProjects}</div>
                  )}
                </div>
              )}
            </div>
          ) : (
            <div className="text-xs text-gray-300 space-y-1">
              <div className="flex items-center justify-between">
                <span className="text-blue-400 font-medium truncate">{session.repositoryName}</span>
                <span className={`px-1 py-0.5 rounded text-[10px] ${fromCache ? 'bg-green-900 text-green-300' : 'bg-yellow-900 text-yellow-300'}`}>{fromCache ? '缓存' : '扫描'}</span>
              </div>
              <div className="text-gray-500 text-[10px] truncate">{session.repositoryPath}</div>
              <div className="text-[10px] text-gray-500">{session.nodeCount}节点 · {session.edgeCount}边</div>
              <button onClick={handleReload} className="w-full text-[10px] bg-gray-800 hover:bg-gray-700 text-gray-400 rounded px-1.5 py-0.5">🔄 重新扫描</button>
            </div>
          )}

          {history.length > 0 && (
            <>
              <h2 className="text-xs font-semibold text-gray-500 uppercase tracking-wider mt-2">历史</h2>
              <div className="space-y-1 max-h-40 overflow-y-auto">
                {history.map((h, i) => (
                  <div key={i} className={`text-[10px] rounded p-1 ${session?.repositoryPath === h.path ? 'bg-blue-900/30' : 'bg-gray-800/30 hover:bg-gray-800'}`}>
                    <button onClick={() => handleLoadFromHistory(h.path)} className="text-left w-full text-gray-300 truncate" title={h.path}>{h.name}</button>
                    <button onClick={() => handleDeleteHistory(h.path)} className="text-gray-600 hover:text-red-400 ml-1">✕</button>
                  </div>
                ))}
              </div>
            </>
          )}

        </aside>

        {/* Main chat */}
        <main className="flex-1 flex flex-col bg-gray-950 min-w-0">
          <div ref={chatRef} className="flex-1 overflow-y-auto p-4 space-y-3">
            {messages.length === 0 && (
              <div className="text-center text-gray-600 mt-20">
                <p className="text-lg mb-2">{session?.isLoaded ? '输入你的需求' : '加载仓库后开始'}</p>
                <p className="text-xs">试试: "解释架构" · "改动 RetryPolicy 影响" · "修改 XXX 方法加参数校验"</p>
              </div>
            )}
            {messages.map((msg, i) => (
              <div key={i} className={`flex ${msg.role === 'user' ? 'justify-end' : 'justify-start'}`}>
                <div className={`max-w-2xl rounded-lg px-3 py-2 text-sm ${msg.role === 'user' ? 'bg-blue-600 text-white' : 'bg-gray-800 text-gray-200'}`}>
                  {msg.role === 'user' ? <p>{msg.content}</p> : <SystemMessage msg={msg} />}
                </div>
              </div>
            ))}
            {loading && !loadProgress && <div className="flex justify-start"><div className="bg-gray-800 rounded-lg px-3 py-2 text-gray-400 text-sm">思考中...</div></div>}
            {loading && loadProgress && (
              <div className="flex justify-center">
                <div className="bg-gray-800/80 rounded-lg px-4 py-2.5 text-sm space-y-1.5 max-w-sm w-full">
                  <div className="flex justify-between text-xs">
                    <span className="text-blue-400">{stageLabel(loadProgress.stage)}</span>
                    <span className="text-gray-500">{Math.round(loadProgress.percent * 100)}%</span>
                  </div>
                  <div className="w-full bg-gray-700 rounded-full h-1.5 overflow-hidden">
                    <div className="bg-blue-500 h-full rounded-full transition-all duration-300" style={{ width: `${Math.round(loadProgress.percent * 100)}%` }} />
                  </div>
                  {loadProgress.totalFiles > 0 && (
                    <div className="text-[10px] text-gray-500 text-center">
                      {loadProgress.currentFilePath
                        ? `${loadProgress.currentFilePath} (${loadProgress.currentFile}/${loadProgress.totalFiles})`
                        : `文件 ${loadProgress.currentFile}/${loadProgress.totalFiles}`}
                    </div>
                  )}
                </div>
              </div>
            )}
          </div>
          {/* Input area */}
          <form onSubmit={handleSend} className="border-t border-gray-800 p-3 bg-gray-900 space-y-2">
            <input value={input} onChange={e => setInput(e.target.value)}
              placeholder={session?.isLoaded
                ? '输入认知查询，如"解释架构"、"改动 RetryPolicy 影响"...'
                : "先加载仓库"}
              disabled={!session?.isLoaded}
              className="w-full bg-gray-800 border border-gray-700 rounded px-3 py-2 text-sm text-gray-200 placeholder-gray-500 focus:outline-none focus:border-blue-500 disabled:opacity-50" />
            <button type="submit" disabled={loading || !session?.isLoaded}
              className="bg-blue-600 hover:bg-blue-700 disabled:opacity-50 text-white text-sm rounded px-4 py-1.5 transition">发送</button>
          </form>
        </main>

        {/* Right details panel */}
        <aside className="w-72 bg-gray-900 border-l border-gray-800 p-3 overflow-y-auto shrink-0 text-xs">
          {lastDetails?.type === 'codefix' ? (
            <div className="space-y-2">
              <h3 className="text-gray-400 font-semibold uppercase text-[10px] tracking-wider">代码修复</h3>
              <div className={`p-2 rounded ${lastDetails.success ? 'bg-green-900/50 border border-green-500/30' : 'bg-red-900/50 border border-red-500/30'}`}>
                <div className={`font-medium ${lastDetails.success ? 'text-green-300' : 'text-red-300'}`}>
                  {lastDetails.success ? '✅ 通过' : '❌ 失败'} ({lastDetails.attempts}次)
                </div>
                <div className="text-gray-400 mt-1">{lastDetails.summary}</div>
              </div>
              {lastDetails.repairHistory?.length > 0 && (
                <details><summary className="text-gray-500 cursor-pointer">修复过程</summary>
                  <div className="space-y-0.5 mt-1 max-h-32 overflow-y-auto">{lastDetails.repairHistory.map((h: string, i: number) => <div key={i} className="text-gray-500">{h}</div>)}</div>
                </details>
              )}
              {lastDetails.buildErrors?.length > 0 && (
                <details><summary className="text-red-400 cursor-pointer">编译错误</summary>
                  <div className="space-y-0.5 mt-1 max-h-32 overflow-y-auto">{lastDetails.buildErrors.map((e: string, i: number) => <div key={i} className="text-red-400">{e}</div>)}</div>
                </details>
              )}
            </div>
          ) : lastDetails?.type === 'query' ? (
            <div className="space-y-2">
              <h3 className="text-gray-400 font-semibold uppercase text-[10px] tracking-wider">证据</h3>
              {lastDetails.citations?.length > 0 ? lastDetails.citations.map((c: any, i: number) => (
                <div key={i} className="p-1.5 bg-gray-800 rounded">
                  <div className="text-gray-300 truncate">{c.label}</div>
                  <div className="text-gray-500 text-[10px] truncate">{c.sourceFile || '(无文件)'}</div>
                  <span className={`text-[10px] px-1 rounded ${c.confidence === 'Certain' || c.confidence === 'Strong' ? 'text-green-400' : 'text-yellow-400'}`}>{c.confidence}</span>
                </div>
              )) : <div className="text-gray-600">查询后显示证据</div>}
            </div>
          ) : (
            <div className="text-gray-600">结果详情在此显示</div>
          )}
        </aside>
      </div>
    </div>
  )
}

function stageLabel(stage: string): string {
  const map: Record<string, string> = {
    discovering: '发现项目...', parsing: '解析文件...', resolving: '语义解析...',
    building_graph: '构建图...', analyzing: '分析中...', caching: '缓存中...', complete: '完成',
  }
  return map[stage] || stage
}

function SystemMessage({ msg }: { msg: Message }) {
  const d = msg.details
  if (!d) return <p>{msg.content}</p>

  if (d.type === 'codefix') {
    return (
      <div className="space-y-1">
        <div className={`font-semibold ${d.success ? 'text-green-400' : 'text-red-400'}`}>
          {d.success ? '✅ 代码修改成功' : '❌ 代码修改失败'}
        </div>
        <p className="text-gray-300">{d.summary}</p>
        <p className="text-gray-500 text-xs">尝试 {d.attempts} 次 · {d.buildErrors?.length || 0} 个编译错误</p>
      </div>
    )
  }

  // Query result
  return (
    <div className="space-y-1.5">
      <div className="flex items-center gap-2">
        <span className="font-semibold">{d.title || '分析结果'}</span>
        <span className="text-xs text-gray-500">[{d.confidence}] · {d.evidenceCount}条证据</span>
      </div>
      {d.explanations?.map((e: any, i: number) => (
        <div key={i} className="text-gray-300">{e.text}</div>
      ))}
      {d.suggestedFollowUps?.length > 0 && (
        <div className="flex gap-1.5 pt-1 border-t border-gray-700">
          {d.suggestedFollowUps.slice(0, 3).map((q: string, i: number) => (
            <button key={i} onClick={() => {
              const input = document.querySelector('input[placeholder]') as HTMLInputElement
              if (input) { input.value = q; input.form?.requestSubmit() }
            }} className="text-[10px] bg-gray-700 hover:bg-gray-600 text-gray-300 rounded-full px-2 py-0.5">{q}</button>
          ))}
        </div>
      )}
    </div>
  )
}
