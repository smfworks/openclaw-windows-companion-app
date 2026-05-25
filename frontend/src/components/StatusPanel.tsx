interface StatusPanelProps {
  status: string;
}

function StatusPanel({ status }: StatusPanelProps) {
  const statusConfig = {
    Running: { color: 'text-green-400', bg: 'bg-green-500/10', border: 'border-green-500/30' },
    Stopped: { color: 'text-gray-400', bg: 'bg-gray-500/10', border: 'border-gray-500/30' },
    Starting: { color: 'text-yellow-400', bg: 'bg-yellow-500/10', border: 'border-yellow-500/30' },
    Error: { color: 'text-red-400', bg: 'bg-red-500/10', border: 'border-red-500/30' },
  };

  const config = statusConfig[status as keyof typeof statusConfig] || statusConfig.Stopped;

  return (
    <div className={`rounded-lg border p-6 ${config.bg} ${config.border}`}>
      <div className="flex items-center justify-between">
        <div>
          <h2 className="text-lg font-semibold text-white">Gateway Status</h2>
          <p className="text-sm text-gray-400 mt-1">OpenClaw gateway service</p>
        </div>
        <div className="text-right">
          <span className={`text-2xl font-bold ${config.color}`}>{status}</span>
          <p className="text-xs text-gray-500 mt-1">localhost:18789</p>
        </div>
      </div>
    </div>
  );
}

export default StatusPanel;
