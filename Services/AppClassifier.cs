namespace ColdStart.Services;

public record AppProfile(
    string Category, bool Essential, string Impact, string Action,
    string Suggestion, string WhatItDoes, string IfDisabled,
    string WhySlow = "", string HowToSpeedUp = "");

public static class AppClassifier
{
    private static readonly Dictionary<string, AppProfile> KnownApps = new(StringComparer.OrdinalIgnoreCase)
    {
        ["onedrive"] = new("Cloud Storage", false, "high", "can_disable",
            "Syncs files in the background. Safe to start manually when needed. Saves 2-4s on boot.",
            "Automatically syncs your Documents, Desktop, and Pictures folders to Microsoft's cloud. Keeps a backup of your files and lets you access them from other devices or the web.",
            "Your files will stop syncing automatically at startup. No files are lost — they stay on your PC and in the cloud. You can open OneDrive manually anytime to sync.",
            "OneDrive indexes thousands of files at startup to detect changes, builds a local sync database, and establishes a persistent connection to Microsoft's cloud servers.",
            "Pause syncing during startup: OneDrive Settings → Sync & backup → pause. Or disable auto-start and open it manually after you've settled in."),

        ["teams"] = new("Communication", false, "high", "can_disable",
            "Heavy app that adds 3-5s to boot. Launches quickly when opened manually.",
            "Microsoft's communication app for workplace chat, video calls, meetings, and file sharing. It runs in the background to give you instant notifications for messages and meeting reminders.",
            "Teams won't open automatically. You'll need to open it yourself before joining meetings or checking messages. Nothing is lost — all messages and meetings will load when you open it.",
            "Teams loads a full Electron/WebView2 runtime, pre-caches chat history, syncs calendar data, and initializes audio/video subsystems — essentially booting a mini web browser.",
            "In Teams Settings → General, uncheck 'Auto-start application'. Open Teams only when you need it — it launches in about 3-5 seconds on its own."),

        ["spotify"] = new("Entertainment", false, "medium", "safe_to_disable",
            "No need to auto-start. Open it when you want to listen to music.",
            "Music and podcast streaming app. When set to auto-start, it sits in your system tray so music is instantly available when you click it.",
            "Spotify simply won't open at login. Open it when you want to listen — it starts in about 2 seconds. No impact on your music or playlists.",
            "Spotify pre-loads its streaming engine, builds a local track cache, and connects to Spotify servers to sync playlists and recommendations.",
            "In Spotify Settings → Startup, set 'Open Spotify automatically' to No. It opens in ~2 seconds when you launch it manually."),

        ["discord"] = new("Communication", false, "medium", "can_disable",
            "Runs in background for notifications. Disable if you don't need instant alerts at boot.",
            "Voice, video, and text chat app popular with gaming communities and friend groups. Runs in the background to deliver instant message and call notifications.",
            "You won't get instant Discord notifications until you manually open the app. All your messages and servers will be waiting when you do. No data is lost.",
            "Discord loads its Electron runtime (essentially a full Chrome browser), connects to voice/chat servers, and syncs message history for all your servers.",
            "In Discord User Settings → Windows Settings, disable 'Open Discord'. Consider using the web version at discord.com to avoid the desktop overhead entirely."),

        ["steam"] = new("Gaming", false, "medium", "safe_to_disable",
            "Game launcher. No need to auto-start — open when you want to play.",
            "Valve's game store and launcher. Auto-starts to download game updates in the background and show notifications about sales and friend activity.",
            "Games won't auto-update in the background. Open Steam when you want to play — updates will download then. No games are affected.",
            "Steam checks for updates across all installed games, initializes its overlay system, connects to Steam servers for friend status, and may start downloading pending updates.",
            "In Steam → Settings → Interface, uncheck 'Run Steam when my computer starts'. Open it only when you're ready to game."),

        ["epicgames"] = new("Gaming", false, "medium", "safe_to_disable",
            "Game launcher. Safe to disable — open when you want to play.",
            "Epic Games Store launcher. Auto-starts to keep games updated and check for free game offers.",
            "Same as not opening it — games won't auto-update. Launch it when you want to play. No games or saves are affected.",
            "Similar to Steam — checks for game updates, syncs library status, and connects to Epic's servers for store notifications.",
            "Open Epic Games Launcher → Settings → uncheck 'Run When My Computer Starts'."),

        ["adobe"] = new("Creative Suite", false, "high", "can_disable",
            "Creative Cloud updater. Very heavy on startup. Updates run when you open any Adobe app.",
            "Adobe Creative Cloud manager that keeps Photoshop, Illustrator, and other Adobe apps updated. Also syncs fonts and creative assets.",
            "Adobe apps still work perfectly — they just won't auto-update. Updates will install when you open any Adobe app. Synced fonts load when you open Creative Cloud.",
            "Adobe CC loads multiple background services (CCXProcess, CCLibrary, AdobeIPCBroker), syncs cloud fonts, checks licenses for all installed Adobe products, and pre-caches creative assets.",
            "In Creative Cloud → Preferences → General, disable 'Launch Creative Cloud at login'. Open it manually when you need to update or sync fonts."),

        ["skype"] = new("Communication", false, "medium", "safe_to_disable",
            "Legacy communication app. Safe to disable if you use Teams or other alternatives.",
            "Microsoft's older video calling and messaging app. Runs in the background for incoming call and message notifications.",
            "No incoming call notifications until you open Skype manually. If you mainly use Teams, Zoom, or your phone, you won't notice any difference.",
            "Skype loads its communication runtime and maintains a persistent connection to Microsoft's notification servers for calls and messages.",
            "In Skype Settings → General, disable 'Automatically start Skype'. If you've moved to Teams or Zoom, consider uninstalling Skype entirely."),

        ["zoom"] = new("Communication", false, "low", "safe_to_disable",
            "Launches quickly from meeting links. No need to auto-start.",
            "Video conferencing app. Auto-start keeps it ready for instant meeting joins.",
            "Clicking a Zoom meeting link will still open Zoom — it just takes 1-2 extra seconds to load first. No meetings are affected.",
            "Zoom's auto-start component is lightweight — it just keeps the app ready in memory for faster meeting joins.",
            "In Zoom Settings → General, uncheck 'Start Zoom when I start Windows'. Meeting links will still work."),

        ["cortana"] = new("Assistant", false, "medium", "can_disable",
            "Voice assistant. Safe to disable if you don't use voice commands.",
            "Microsoft's voice assistant that listens for \"Hey Cortana\" commands and can set reminders, search the web, and control settings by voice.",
            "Voice commands stop working until you open Cortana manually. If you don't use voice commands, you won't notice any difference.",
            "Cortana loads speech recognition models and keeps a persistent microphone listener running, consuming CPU and memory even when idle.",
            "Search 'Cortana' in Windows Settings → disable 'Let Cortana respond to Hey Cortana'. This stops the always-listening mode."),

        ["chrome"] = new("Browser", false, "medium", "safe_to_disable",
            "Browser background process. Safe to disable — Chrome opens fast on its own.",
            "Google Chrome's background service that pre-loads browser components and enables notifications from websites even when Chrome is closed.",
            "Chrome takes about 1 extra second to open the first time. Web push notifications won't appear until Chrome is running. Browsing works normally.",
            "Chrome pre-loads its V8 JavaScript engine and network stack into memory, and maintains connections for web push notifications from all subscribed sites.",
            "In Chrome → Settings → System, disable 'Continue running background apps when Chrome is closed'."),

        ["googlechrome"] = new("Browser", false, "medium", "safe_to_disable",
            "Browser background process. Safe to disable — Chrome opens fast on its own.",
            "Google Chrome's background service that pre-loads browser components for faster launch.",
            "Chrome takes about 1 extra second to open the first time. Everything else works normally.",
            "Same as Chrome — pre-loads browser engine and maintains notification connections in the background.",
            "In Chrome Settings → System, turn off 'Continue running background apps when Google Chrome is closed'."),

        ["msedge"] = new("Browser", false, "low", "can_disable",
            "Edge startup boost. Can be disabled if Edge feels fast enough without it.",
            "Microsoft Edge's startup accelerator that pre-loads parts of the browser into memory so it opens faster when you click it.",
            "Edge will take a moment longer to open the first time. If you don't use Edge as your main browser, disabling this is a no-brainer.",
            "Edge's startup boost pre-loads the browser engine into memory at login. Lightweight but unnecessary if Edge isn't your default browser.",
            "In Edge → Settings → System → 'Startup boost', toggle it off."),

        ["slack"] = new("Communication", false, "medium", "can_disable",
            "Workspace messaging. Consider disabling if you don't need instant notifications at boot.",
            "Workplace messaging and collaboration app. Runs in the background to deliver instant notifications for direct messages, mentions, and channel updates.",
            "You won't get Slack notifications until you open the app. All messages will be waiting for you. Good to disable if you check Slack on your own schedule.",
            "Slack loads an Electron runtime (essentially a full Chrome browser), connects to multiple workspace servers, and syncs unread messages across all your channels.",
            "In Slack → Preferences → Windows Settings, disable 'Launch Slack on login'. Pin it to your taskbar for quick access instead."),

        ["dropbox"] = new("Cloud Storage", false, "high", "can_disable",
            "Cloud sync. Heavy on boot. Can start manually when needed.",
            "Cloud file syncing service that keeps your Dropbox folder synchronized across all your devices. Monitors files for changes and uploads/downloads them.",
            "Files stop syncing automatically. Your files remain on your PC and in the cloud — nothing is deleted. Open Dropbox when you need to sync.",
            "Dropbox indexes your entire sync folder, establishes cloud connections, and starts monitoring all file changes — especially heavy if you have thousands of synced files.",
            "Reduce synced folders via Dropbox Preferences → Sync → Selective Sync. Or switch to 'Online-only' mode."),

        ["itunes"] = new("Entertainment", false, "medium", "safe_to_disable",
            "Apple media service. Safe to disable — open when needed.",
            "Apple's media manager for music, podcasts, and iOS device syncing. Auto-starts helper services for device detection.",
            "Your iPhone/iPad won't be detected instantly when plugged in — it'll take a few seconds for iTunes to start. Music library is unaffected.",
            "iTunes loads Apple Mobile Device Service and Bonjour for device detection, plus its media library index.",
            "Uninstall Bonjour if you don't use AirPlay. In iTunes Preferences → Advanced, uncheck auto-launch options."),

        ["nvidia"] = new("GPU Driver", true, "low", "keep",
            "GPU driver component. Keep enabled for optimal graphics performance.",
            "NVIDIA's graphics driver software that manages your GPU, handles display output, and provides features like GeForce Experience for game optimization.",
            "⚠️ Not recommended. Your display may not work correctly, games may run poorly, and GPU features will be unavailable until next restart.",
            "NVIDIA's driver components are lightweight and essential for GPU management. The GeForce Experience overlay adds most overhead.",
            "Keep the core driver but uninstall GeForce Experience if you don't use game optimization or ShadowPlay recording."),

        ["realtek"] = new("Audio Driver", true, "low", "keep",
            "Audio driver. Keep enabled for sound to work properly.",
            "Audio driver that manages your computer's speakers, headphone jack, and microphone. Controls sound processing and audio enhancements.",
            "⚠️ Not recommended. You may lose sound or microphone functionality until you restart your computer.",
            "Audio drivers are lightweight and load quickly — they need to be ready before any app tries to play sound.",
            "Already fast. If you experience audio delays, check for driver updates from your manufacturer's website."),

        ["defender"] = new("Security", true, "low", "keep",
            "Essential security protection. Do not disable.",
            "Windows Defender antivirus that continuously scans for malware, viruses, and threats. Provides real-time protection for your files, downloads, and apps.",
            "⚠️ Do not disable. Your computer will be unprotected against malware and viruses. Windows may re-enable it automatically.",
            "Defender loads virus definition databases and initializes real-time file monitoring — essential and optimized by Microsoft.",
            "Keep enabled. If scans feel slow, add exclusions in Windows Security → Virus & threat protection → Manage settings → Exclusions."),

        ["securityhealth"] = new("Security", true, "low", "keep",
            "Windows Security service. Keep enabled.",
            "Windows Security monitoring service that checks your antivirus, firewall, and update status. Shows security alerts in your notification area.",
            "⚠️ Not recommended. You won't receive security warnings about threats, outdated antivirus, or firewall issues.",
            "Very lightweight system health monitor — minimal impact on boot time.",
            "Already fast. No action needed."),

        ["malwarebytes"] = new("Security", true, "medium", "keep",
            "Anti-malware protection. Keep enabled for security.",
            "Third-party anti-malware software that provides additional protection against threats that may slip past Windows Defender.",
            "⚠️ Not recommended. You lose an extra layer of malware protection. Only disable if you trust Windows Defender alone.",
            "Malwarebytes loads its real-time protection engine and web filtering module, which intercepts all network traffic and file operations.",
            "In Malwarebytes Settings, disable 'Web Protection' if you don't need it."),

        ["vmware"] = new("Virtualization", false, "medium", "can_disable",
            "VM tools. Disable if you don't use virtual machines daily.",
            "VMware virtualization software services. Runs background processes to support virtual machine networking and USB passthrough.",
            "Virtual machines won't start as quickly, and some VM network features may need to be started manually. Only matters if you actively use VMs.",
            "VMware loads multiple services (vmnat, vmnetdhcp, VMAuthdService) for virtual networking even when no VMs are running.",
            "Set VMware services to 'Manual' start in Windows Services (services.msc). They'll start automatically when you launch a VM."),

        ["java"] = new("Runtime", false, "low", "safe_to_disable",
            "Java updater. Safe to disable — updates can be checked manually.",
            "Java runtime auto-updater that periodically checks for new Java versions and security patches.",
            "Java stops auto-updating. You can check for updates manually through the Java Control Panel. Java apps continue to work normally.",
            "Java's update scheduler is lightweight but runs an unnecessary background check at every boot.",
            "Disable the update scheduler here. Check for Java updates manually once a month at java.com."),

        ["ccleaner"] = new("Utility", false, "low", "safe_to_disable",
            "System cleaner. Run manually when needed.",
            "System cleaning utility that can remove temporary files, browser cache, and registry entries. The startup component monitors your system.",
            "CCleaner won't run its monitoring in the background. You can still open and run it manually whenever you want to clean up.",
            "CCleaner's monitoring agent continuously watches for file changes and browser activity to offer cleanup suggestions.",
            "Open CCleaner → Options → Startup → disable background monitoring. Run it manually once a week instead."),

        ["github"] = new("Development", false, "low", "safe_to_disable",
            "GitHub Desktop. Safe to disable — open when you need it.",
            "GitHub's desktop application for managing Git repositories with a visual interface. Auto-starts to keep repositories synchronized.",
            "Repositories won't sync in the background. Open GitHub Desktop when you need it — it syncs quickly on launch.",
            "GitHub Desktop checks for repository changes across all your cloned repos on startup and maintains notification connections.",
            "Open it from your taskbar or Start menu when you need it. It syncs in seconds on launch."),

        ["notion"] = new("Productivity", false, "low", "safe_to_disable",
            "Note-taking app. Safe to disable — opens quickly when needed.",
            "Note-taking and project management app. Auto-starts to provide quick access and offline sync of your notes and databases.",
            "Notion won't be instantly available in your system tray. Open it when needed — it launches in a few seconds.",
            "Notion syncs offline content databases and pre-caches recently viewed pages at startup.",
            "Disable auto-start in Notion → Settings → App. It launches fast and syncs quickly on demand."),

        ["figma"] = new("Design", false, "low", "safe_to_disable",
            "Design tool agent. Safe to disable.",
            "Figma's desktop agent that enables font access for the web app and provides desktop notifications for design file comments.",
            "The Figma website won't be able to access your local fonts until you open Figma Desktop. Design files are unaffected.",
            "Figma Agent scans and indexes all your local fonts so the web app can use them. Lightweight but unnecessary when you're not designing.",
            "Disable auto-start and open Figma Agent only when working on designs that need local fonts."),

        ["whatsapp"] = new("Communication", false, "medium", "can_disable",
            "Messaging app. Disable if you primarily use it on your phone.",
            "WhatsApp Desktop mirrors your phone's WhatsApp conversations. Runs in the background to deliver message and call notifications on your PC.",
            "You won't receive WhatsApp notifications on your PC until you open the app. Messages still arrive on your phone normally.",
            "WhatsApp Desktop syncs message history from your phone and maintains a persistent encrypted connection.",
            "Open WhatsApp Desktop from your taskbar only when you want to chat on your PC."),

        ["telegram"] = new("Communication", false, "low", "safe_to_disable",
            "Messaging app. Safe to disable — open when needed.",
            "Cloud-based messaging app. Auto-starts for instant message notifications on your desktop.",
            "No desktop notifications until you open Telegram. Messages are stored in the cloud and load instantly when you open the app.",
            "Telegram is relatively lightweight — connects to cloud servers and syncs recent chat messages.",
            "In Telegram Settings → Advanced → disable 'Launch Telegram at system startup'."),

        ["logitech"] = new("Peripheral", false, "low", "review",
            "Peripheral software. Keep if you use custom button/macro configurations.",
            "Logitech device manager (Options/G Hub) that controls custom button mappings, scroll settings, and lighting on your Logitech mouse, keyboard, or headset.",
            "Custom button mappings and macros won't work until you open the Logitech software. Default mouse and keyboard functions still work normally.",
            "Logitech Options/G Hub scans for connected devices, loads custom profiles, and initializes lighting — heavier with more devices.",
            "Reduce profiles to only your most-used devices. In G Hub → Settings → 'Run on startup' can be toggled."),

        ["razer"] = new("Peripheral", false, "medium", "review",
            "Gaming peripheral software. Keep if you use custom lighting/macros.",
            "Razer Synapse manages lighting effects, macro keys, and DPI settings for Razer mice, keyboards, and headsets.",
            "RGB lighting reverts to default, and custom key bindings/macros won't work until you open Razer Synapse. Basic device functions work normally.",
            "Razer Synapse loads Chroma RGB engine, initializes device profiles for all connected peripherals, and syncs settings from the cloud.",
            "Save profiles directly to device memory (on-board storage) in Synapse, then you can disable Synapse startup."),

        ["corsair"] = new("Peripheral", false, "medium", "review",
            "Peripheral software. Keep if you use custom configurations.",
            "Corsair iCUE controls RGB lighting, fan speeds, and macro keys for Corsair peripherals and PC components.",
            "RGB lighting and fan profiles revert to hardware defaults. Custom macros won't work until iCUE is opened. Basic device functions are unaffected.",
            "iCUE loads lighting engines, fan curve controllers, and device communication layers for all Corsair hardware.",
            "Store profiles in hardware memory via iCUE → device → 'Hardware Lighting'. Then skip iCUE on startup."),

        ["overwolf"] = new("Gaming", false, "medium", "safe_to_disable",
            "Gaming overlay platform. Safe to disable if not actively used.",
            "Gaming overlay platform that provides in-game apps, stats tracking, and recording tools for various games.",
            "In-game overlays and Overwolf apps won't be available until you open Overwolf. Games run normally — often better without overlays.",
            "Overwolf loads its overlay engine, hooks into DirectX/Vulkan rendering pipelines, and initializes in-game app sandboxes — can reduce game FPS.",
            "Uninstall if not actively using Overwolf apps. If needed, open it manually before launching that game."),

        ["battlenet"] = new("Gaming", false, "medium", "safe_to_disable",
            "Game launcher. No need to auto-start.",
            "Blizzard's game launcher for World of Warcraft, Overwatch, Diablo, and other Blizzard games. Checks for game updates in the background.",
            "Games won't auto-update. Open Battle.net when you want to play — updates download then. Game saves are unaffected.",
            "Battle.net checks for updates across all installed Blizzard games and connects to social/friend status servers.",
            "In Battle.net → Settings → General, disable 'Launch Battle.net when I start my computer'."),
    };

    private static readonly string[] EssentialPatterns =
    {
        "security", "antivirus", "firewall", "antimalware",
        "audio service", "bluetooth support", "cryptographic",
        "dhcp", "dns client", "network connection", "plug and play",
        "remote procedure call", "windows event log",
        "windows management instrumentation", "windows update",
        "shell hardware detection", "system event notification",
        "task scheduler", "user profile service", "windows audio",
        "windows font", "windows time",
    };

    public static AppProfile Classify(string name, string command = "", string publisher = "", string description = "")
    {
        var blob = $"{name}{command}{publisher}{description}".Replace(" ", "");

        foreach (var (keyword, profile) in KnownApps)
        {
            if (blob.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                return profile;
        }

        var fullText = $"{name} {description}".ToLowerInvariant();
        foreach (var pattern in EssentialPatterns)
        {
            if (fullText.Contains(pattern))
                return new AppProfile("System", true, "low", "keep",
                    "Core system component. Keep enabled.",
                    "A Windows system service required for your computer to function properly. It handles essential behind-the-scenes operations.",
                    "⚠️ Not recommended. Disabling system components can cause instability, loss of features, or prevent Windows from working correctly.",
                    "System services are lightweight and load quickly — they're already optimized by Windows.",
                    "Already optimized. No action needed.");
        }

        if (publisher.Contains("Microsoft", StringComparison.OrdinalIgnoreCase))
            return new AppProfile("Microsoft", false, "low", "review",
                "Microsoft component. Review if you use this feature.",
                $"A Microsoft component ({name}) that supports a Windows feature or service. It may provide background functionality you use.",
                "The associated Windows feature may not work automatically. If you don't use this feature, you won't notice any difference.",
                "Microsoft background components are generally lightweight but may check for updates or sync data at startup.",
                "If you don't use this feature, disabling it is safe. Otherwise, no action needed.");

        return new AppProfile("Third Party", false, "medium", "review",
            "Third-party app. Disable if you don't use it daily.",
            $"{name} is a third-party application that has registered itself to start automatically with Windows.",
            "This app won't start automatically. You can always open it manually when needed. No data will be lost.",
            "Third-party apps may load background services, check for updates, or initialize connections at startup.",
            "Check the app's settings for a 'Run at startup' or 'Launch on login' option and disable it.");
    }
}
