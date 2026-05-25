import { useState, useEffect } from 'react'
import { GetGatewayStatus, StartGateway, StopGateway, RestartGateway, GetGatewayLogs, GetSettings, SaveSettings } from '../wailsjs/go/main/App'
import { BrowserOpenURL, Quit } from '../wailsjs/runtime'
import StatusPanel from './components/StatusPanel'
import LogViewer from './components/LogViewer'
import ControlPanel from './components/ControlPanel'
import SettingsPanel from './components/SettingsPanel'
import './style.css'

function App() {
  const [status, setStatus] = useState<string>('Stopped')
  const [logs, setLogs] = useState<string[]>([])
  const [activeTab, setActiveTab] = useState<'status' | 'logs' | 'settings'>('status')

  const refreshStatus = async () => {
    try {
      const result = await GetGatewayStatus()
      setStatus(result)
    } catch (e) {
      setStatus('Error')
    }
  }

  const refreshLogs = async () => {
    try {
      const result = await GetGatewayLogs(100)
      setLogs(result)
    } catch (e) {
      console.error('Failed to fetch logs:', e)
    }
  }

  const handleStart = async () => {
    try {
      await StartGateway()
      refreshStatus()
    } catch (e) {
      console.error('Start failed:', e)
    }
  }

  const handleStop = async () => {
    try {
      await StopGateway()
      refreshStatus()
    } catch (e) {
      console.error('Stop failed:', e)
    }
  }

  const handleRestart = async () => {
    try {
      await RestartGateway()
      refreshStatus()
    } catch (e) {
      console.error('Restart failed:', e)
    }
  }

  useEffect(() => {
    refreshStatus()
    const interval = setInterval(() => {
      refreshStatus()
      refreshLogs()
    }, 2000)
    return () => clearInterval(interval)
  }, [])

  return (
    <div className="min-h-screen bg-gray-900 text-white">
      <header className="bg-gray-800 border-b border-gray-700 px-6 py-4">
        <div className="flex items-center justify-between">
          <div className="flex items-center gap-3">
            <div className={`w-3 h-3 rounded-full ${
              status === 'Running' ? 'bg-green-500' : 
              status === 'Error' ? 'bg-red-500' : 
              status === 'Starting' ? 'bg-yellow-500' : 'bg-gray-500'
            }`} />
            <h1 className="text-xl font-bold">OpenClaw Companion</h1>
          </div>
          <div className="flex items-center gap-4">
            <span className="text-sm text-gray-400">v0.1.0</span>
            <button 
              onClick={() => BrowserOpenURL('https://openclaw.ai')}
              className="text-sm text-blue-400 hover:text-blue-300"
            >
              Docs
            </button>
            <button 
              onClick={Quit}
              className="text-sm text-gray-400 hover:text-white"
            >
              Exit
            </button>
          </div>
        </div>
      </header>

      <nav className="bg-gray-800 border-b border-gray-700 px-6">
        <div className="flex gap-1">
          {(['status', 'logs', 'settings'] as const).map((tab) => (
            <button
              key={tab}
              onClick={() => setActiveTab(tab)}
              className={`px-4 py-3 text-sm font-medium capitalize transition-colors ${
                activeTab === tab
                  ? 'text-white border-b-2 border-blue-500'
                  : 'text-gray-400 hover:text-white'
              }`}
            >
              {tab}
            </button>
          ))}
        </div>
      </nav>

      <main className="p-6">
        {activeTab === 'status' && (
          <div className="space-y-6">
            <StatusPanel status={status} />
            <ControlPanel
              onStart={handleStart}
              onStop={handleStop}
              onRestart={handleRestart}
              status={status}
            />
          </div>
        )}
        {activeTab === 'logs' && <LogViewer logs={logs} />}
        {activeTab === 'settings' && <SettingsPanel />}
      </main>
    </div>
  )
}

export default App
