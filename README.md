# ⚡ ColdStart — Startup Performance Inspector

A Windows desktop app that identifies what's slowing down your PC's startup and helps you take action.

![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4) ![WPF](https://img.shields.io/badge/WPF-Desktop-blue) ![Windows](https://img.shields.io/badge/Platform-Windows-0078D6)

## What it does

ColdStart scans your Windows startup configuration and gives you a clear picture of:

- **Which apps launch at startup** — from Registry, Startup Folder, Scheduled Tasks, Services, and UWP apps
- **How long each one delays your boot** — using real Windows Event Log diagnostics and process timing
- **What you can safely disable** — categorized by impact level with clear recommendations
- **Why each app is slow** — per-app explanations of what makes it heavy
- **How to speed it up** — actionable tips for each startup item

## Features

- 📊 **Visual Timeline** — See your boot sequence on an interactive timeline with phase markers
- 🏷️ **Smart Grouping** — Apps grouped by impact: Critical, High, Medium, Low
- 🔍 **Deep Analysis** — Three timing sources: Windows boot diagnostics, process start times, and heuristic estimates
- 🎨 **Theme Support** — Multiple color themes with consistent styling
- ⚡ **Actionable Insights** — "Why it's slow" and "How to speed it up" for 30+ known apps
- ♿ **Keyboard Accessible** — Navigate tabs with Ctrl+1/2/3

## Screenshots

*Run the app to see it in action — it analyzes your actual startup configuration.*

## Getting Started

### Prerequisites

- Windows 10/11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Run as **Administrator** for full boot diagnostics access

### Build & Run

```bash
dotnet build
dotnet run
```

> **Note:** Some features (Event Log boot diagnostics, service enumeration) require elevated privileges. Run as Administrator for the most accurate results.

## How It Works

1. **Discovery** — Scans Registry run keys, Startup folder, Task Scheduler, Windows Services, and UWP startup tasks
2. **Timing** — Correlates items with Windows Event Log boot traces (EventID 100/101) and running process start times
3. **Classification** — Matches items against 30+ known app profiles to determine impact and provide recommendations
4. **Presentation** — Renders an interactive dashboard with grouping, timeline visualization, and per-app insights

## Tech Stack

- **C# / .NET 8** — Windows desktop target
- **WPF** — UI framework with code-behind rendering
- **System.Management** — WMI queries for startup task discovery
- **System.Diagnostics.EventLog** — Windows boot diagnostic events

## License

MIT
