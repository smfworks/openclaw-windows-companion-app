import { useState, useEffect } from 'react';
import { GetSettings, SaveSettings } from '../../wailsjs/go/main/App';

interface Settings {
  gatewayHost: string;
  gatewayPort: string;
  autoStart: boolean;
  minimizeToTray: boolean;
  darkMode: boolean;
  checkInterval: string;
}

function SettingsPanel() {
  const [settings, setSettings] = useState<Settings>({
    gatewayHost: 'localhost',
    gatewayPort: '18789',
    autoStart: false,
    minimizeToTray: true,
    darkMode: true,
    checkInterval: '2000',
  });
  const [saved, setSaved] = useState(false);

  useEffect(() => {
    loadSettings();
  }, []);

  const loadSettings = async () => {
    try {
      const result = await GetSettings();
      if (result) {
        setSettings(JSON.parse(result));
      }
    } catch (e) {
      console.error('Failed to load settings:', e);
    }
  };

  const handleSave = async () => {
    try {
      await SaveSettings(JSON.stringify(settings));
      setSaved(true);
      setTimeout(() => setSaved(false), 2000);
    } catch (e) {
      console.error('Failed to save settings:', e);
    }
  };

  const updateSetting = <K extends keyof Settings>(key: K, value: Settings[K]) => {
    setSettings(prev => ({ ...prev, [key]: value }));
  };

  return (
    <div className="space-y-6">
      {/* Gateway Settings */}
      <div className="rounded-lg border border-gray-700 bg-gray-800 p-6">
        <h2 className="text-lg font-semibold text-white mb-4">Gateway Configuration</h2>
        <div className="grid grid-cols-2 gap-4">
          <div>
            <label className="block text-sm font-medium text-gray-400 mb-2">Host</label>
            <input
              type="text"
              value={settings.gatewayHost}
              onChange={(e) => updateSetting('gatewayHost', e.target.value)}
              className="w-full px-3 py-2 bg-gray-700 border border-gray-600 rounded-lg text-white focus:outline-none focus:border-blue-500"
            />
          </div>
          <div>
            <label className="block text-sm font-medium text-gray-400 mb-2">Port</label>
            <input
              type="text"
              value={settings.gatewayPort}
              onChange={(e) => updateSetting('gatewayPort', e.target.value)}
              className="w-full px-3 py-2 bg-gray-700 border border-gray-600 rounded-lg text-white focus:outline-none focus:border-blue-500"
            />
          </div>
        </div>
      </div>

      {/* App Behavior */}
      <div className="rounded-lg border border-gray-700 bg-gray-800 p-6">
        <h2 className="text-lg font-semibold text-white mb-4">App Behavior</h2>
        <div className="space-y-4">
          <label className="flex items-center gap-3">
            <input
              type="checkbox"
              checked={settings.autoStart}
              onChange={(e) => updateSetting('autoStart', e.target.checked)}
              className="w-4 h-4 rounded border-gray-600 bg-gray-700 text-blue-600"
            />
            <span className="text-sm text-gray-300">Auto-start gateway on launch</span>
          </label>
          <label className="flex items-center gap-3">
            <input
              type="checkbox"
              checked={settings.minimizeToTray}
              onChange={(e) => updateSetting('minimizeToTray', e.target.checked)}
              className="w-4 h-4 rounded border-gray-600 bg-gray-700 text-blue-600"
            />
            <span className="text-sm text-gray-300">Minimize to system tray instead of closing</span>
          </label>
        </div>
      </div>

      {/* Appearance */}
      <div className="rounded-lg border border-gray-700 bg-gray-800 p-6">
        <h2 className="text-lg font-semibold text-white mb-4">Appearance</h2>
        <div className="space-y-4">
          <label className="flex items-center gap-3">
            <input
              type="checkbox"
              checked={settings.darkMode}
              onChange={(e) => updateSetting('darkMode', e.target.checked)}
              className="w-4 h-4 rounded border-gray-600 bg-gray-700 text-blue-600"
            />
            <span className="text-sm text-gray-300">Dark mode</span>
          </label>
          <div>
            <label className="block text-sm font-medium text-gray-400 mb-2">Status Check Interval (ms)</label>
            <input
              type="number"
              value={settings.checkInterval}
              onChange={(e) => updateSetting('checkInterval', e.target.value)}
              className="w-full px-3 py-2 bg-gray-700 border border-gray-600 rounded-lg text-white focus:outline-none focus:border-blue-500"
            />
          </div>
        </div>
      </div>

      <div className="flex justify-end">
        <button
          onClick={handleSave}
          className="px-6 py-2 bg-blue-600 hover:bg-blue-700 text-white rounded-lg font-medium transition-colors"
        >
          {saved ? 'Saved!' : 'Save Settings'}
        </button>
      </div>
    </div>
  );
}

export default SettingsPanel;
