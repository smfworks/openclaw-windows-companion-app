interface LogViewerProps {
  logs: string[];
}

function LogViewer({ logs }: LogViewerProps) {
  return (
    <div className="rounded-lg border border-gray-700 bg-gray-800 p-6">
      <div className="flex items-center justify-between mb-4">
        <h2 className="text-lg font-semibold text-white">Gateway Logs</h2>
        <span className="text-xs text-gray-500">{logs.length} lines</span>
      </div>
      <div className="bg-gray-900 rounded-lg p-4 h-96 overflow-y-auto font-mono text-sm">
        {logs.length === 0 ? (
          <p className="text-gray-500">No logs available...</p>
        ) : (
          logs.map((log, index) => (
            <div key={index} className="text-gray-300 py-0.5">
              <span className="text-gray-600 mr-2">[{index + 1}]</span>
              {log}
            </div>
          ))
        )}
      </div>
    </div>
  );
}

export default LogViewer;
