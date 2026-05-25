interface ControlPanelProps {
  onStart: () => void;
  onStop: () => void;
  onRestart: () => void;
  status: string;
}

function ControlPanel({ onStart, onStop, onRestart, status }: ControlPanelProps) {
  const isRunning = status === 'Running';
  const isStarting = status === 'Starting';

  return (
    <div className="rounded-lg border border-gray-700 bg-gray-800 p-6">
      <h2 className="text-lg font-semibold text-white mb-4">Controls</h2>
      <div className="flex gap-3">
        <button
          onClick={onStart}
          disabled={isRunning || isStarting}
          className={`px-4 py-2 rounded-lg font-medium transition-colors ${
            isRunning || isStarting
              ? 'bg-gray-700 text-gray-500 cursor-not-allowed'
              : 'bg-green-600 hover:bg-green-700 text-white'
          }`}
        >
          {isStarting ? 'Starting...' : 'Start Gateway'}
        </button>
        
        <button
          onClick={onStop}
          disabled={!isRunning && !isStarting}
          className={`px-4 py-2 rounded-lg font-medium transition-colors ${
            !isRunning && !isStarting
              ? 'bg-gray-700 text-gray-500 cursor-not-allowed'
              : 'bg-red-600 hover:bg-red-700 text-white'
          }`}
        >
          Stop Gateway
        </button>
        
        <button
          onClick={onRestart}
          disabled={isStarting}
          className={`px-4 py-2 rounded-lg font-medium transition-colors ${
            isStarting
              ? 'bg-gray-700 text-gray-500 cursor-not-allowed'
              : 'bg-blue-600 hover:bg-blue-700 text-white'
          }`}
        >
          Restart
        </button>
      </div>
    </div>
  );
}

export default ControlPanel;
