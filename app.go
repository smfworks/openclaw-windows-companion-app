package main

import (
	"context"
	"encoding/json"
	"fmt"
	"net/http"
	"os/exec"
	"strconv"
	"strings"
	"sync"
	"time"

	"github.com/wailsapp/wails/v2/pkg/runtime"
)

// App struct
type App struct {
	ctx      context.Context
	settings Settings
	logs     []string
	mu       sync.RWMutex
}

// Settings holds app configuration
type Settings struct {
	GatewayHost    string `json:"gatewayHost"`
	GatewayPort    string `json:"gatewayPort"`
	AutoStart      bool   `json:"autoStart"`
	MinimizeToTray bool   `json:"minimizeToTray"`
	DarkMode       bool   `json:"darkMode"`
	CheckInterval  string `json:"checkInterval"`
}

// NewApp creates a new App application struct
func NewApp() *App {
	return &App{
		settings: Settings{
			GatewayHost:    "localhost",
			GatewayPort:    "18789",
			AutoStart:      false,
			MinimizeToTray: true,
			DarkMode:       true,
			CheckInterval:  "2000",
		},
		logs: make([]string, 0),
	}
}

// startup is called when the app starts
func (a *App) startup(ctx context.Context) {
	a.ctx = ctx
	a.addLog("OpenClaw Companion started")
}

// addLog adds a log entry
func (a *App) addLog(msg string) {
	a.mu.Lock()
	defer a.mu.Unlock()
	timestamp := time.Now().Format("2006-01-02 15:04:05")
	a.logs = append(a.logs, fmt.Sprintf("[%s] %s", timestamp, msg))
	if len(a.logs) > 1000 {
		a.logs = a.logs[len(a.logs)-1000:]
	}
}

// GetGatewayStatus returns the current gateway status
func (a *App) GetGatewayStatus() string {
	a.mu.RLock()
	port := a.settings.GatewayPort
	a.mu.RUnlock()

	client := &http.Client{Timeout: 2 * time.Second}
	resp, err := client.Get(fmt.Sprintf("http://localhost:%s/status", port))
	if err != nil {
		return "Stopped"
	}
	defer resp.Body.Close()

	if resp.StatusCode == 200 {
		return "Running"
	}
	return "Error"
}

// StartGateway starts the OpenClaw gateway
func (a *App) StartGateway() string {
	a.addLog("Starting OpenClaw gateway...")
	cmd := exec.Command("powershell", "-Command", "Start-Process openclaw -ArgumentList 'gateway start' -WindowStyle Hidden")
	err := cmd.Start()
	if err != nil {
		a.addLog(fmt.Sprintf("Failed to start gateway: %v", err))
		return fmt.Sprintf("Error: %v", err)
	}
	a.addLog("Gateway start command executed")
	return "Starting"
}

// StopGateway stops the OpenClaw gateway
func (a *App) StopGateway() string {
	a.addLog("Stopping OpenClaw gateway...")
	cmd := exec.Command("powershell", "-Command", "Stop-Process -Name openclaw -Force -ErrorAction SilentlyContinue")
	err := cmd.Run()
	if err != nil {
		a.addLog(fmt.Sprintf("Gateway stop warning: %v", err))
	}
	a.addLog("Gateway stop command executed")
	return "Stopped"
}

// RestartGateway restarts the OpenClaw gateway
func (a *App) RestartGateway() string {
	a.addLog("Restarting OpenClaw gateway...")
	a.StopGateway()
	time.Sleep(2 * time.Second)
	return a.StartGateway()
}

// GetGatewayLogs returns recent gateway logs
func (a *App) GetGatewayLogs(lines int) []string {
	a.mu.RLock()
	defer a.mu.RUnlock()

	if len(a.logs) == 0 {
		return []string{"No logs available yet..."}
	}

	if lines >= len(a.logs) {
		return a.logs
	}
	return a.logs[len(a.logs)-lines:]
}

// GetSettings returns current settings as JSON
func (a *App) GetSettings() string {
	a.mu.RLock()
	defer a.mu.RUnlock()

	data, err := json.Marshal(a.settings)
	if err != nil {
		return "{}"
	}
	return string(data)
}

// SaveSettings saves settings from JSON
func (a *App) SaveSettings(settingsJSON string) string {
	var newSettings Settings
	err := json.Unmarshal([]byte(settingsJSON), &newSettings)
	if err != nil {
		return fmt.Sprintf("Error: %v", err)
	}

	a.mu.Lock()
	a.settings = newSettings
	a.mu.Unlock()

	a.addLog("Settings updated")
	return "OK"
}

// Greet returns a greeting (original example method)
func (a *App) Greet(name string) string {
	return fmt.Sprintf("Hello %s, Welcome to OpenClaw Companion!", name)
}

// OpenExternalURL opens a URL in the default browser
func (a *App) OpenExternalURL(url string) {
	runtime.BrowserOpenURL(a.ctx, url)
}

// HideWindow hides the main window
func (a *App) HideWindow() {
	runtime.WindowHide(a.ctx)
}

// ShowWindow shows the main window
func (a *App) ShowWindow() {
	runtime.WindowShow(a.ctx)
}

// MinimizeWindow minimizes to tray
func (a *App) MinimizeWindow() {
	a.mu.RLock()
	minimize := a.settings.MinimizeToTray
	a.mu.RUnlock()

	if minimize {
		runtime.WindowHide(a.ctx)
	} else {
		runtime.WindowMinimise(a.ctx)
	}
}

// GetGatewayPID returns the gateway process PID if running
func (a *App) GetGatewayPID() int {
	cmd := exec.Command("powershell", "-Command", "(Get-Process openclaw -ErrorAction SilentlyContinue).Id")
	output, err := cmd.Output()
	if err != nil {
		return 0
	}
	pidStr := strings.TrimSpace(string(output))
	if pidStr == "" {
		return 0
	}
	pid, err := strconv.Atoi(pidStr)
	if err != nil {
		return 0
	}
	return pid
}
