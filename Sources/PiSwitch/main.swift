import Cocoa

// MARK: - Config
struct PieSwitcherConfig: Codable {
    let apps: [String]
    let colors: [String: String]?
    let labels: [String: String]?
}

// MARK: - Instance
var instanceName = "default"

func namespacePrefix() -> String {
    let raw = ProcessInfo.processInfo.environment["PISWITCH_NAMESPACE"] ?? "piswitch"
    let trimmed = raw.trimmingCharacters(in: .whitespacesAndNewlines)
    if trimmed.isEmpty { return "piswitch" }
    return trimmed
}

func envFlag(_ name: String) -> Bool {
    guard let raw = ProcessInfo.processInfo.environment[name] else { return false }
    switch raw.trimmingCharacters(in: .whitespacesAndNewlines).lowercased() {
    case "1", "true", "yes", "on":
        return true
    default:
        return false
    }
}

let debugLoggingEnabled = envFlag("PISWITCH_DEBUG_LOGS")
let windowReuseEnabled = !envFlag("PISWITCH_DISABLE_WINDOW_REUSE")
let triggerDebounceEnabled = !envFlag("PISWITCH_DISABLE_TRIGGER_DEBOUNCE")
let triggerDebounceInterval: CFTimeInterval = 0.08
let preventIdleSleep = envFlag("PISWITCH_PREVENT_IDLE_SLEEP")

// Default app configurations (name -> color, displayName)
let defaultAppConfigs: [String: (color: NSColor, displayName: String)] = [
    "Codex": (.systemBlue, "Codex"),
    "Claude": (.systemOrange, "Claude"),
    "Claude Code": (.systemOrange, "Claude Code"),
    "Claude Desktop": (.systemOrange, "Claude"),
    "iTerm": (.systemGreen, "iTerm"),
    "iTerm2": (.systemGreen, "iTerm"),
    "Terminal": (.systemGreen, "Terminal"),
    "Visual Studio Code": (.systemBlue, "VS Code"),
    "Code": (.systemBlue, "VS Code"),
    "Safari": (.systemCyan, "Safari"),
    "Firefox": (.systemOrange, "Firefox"),
    "Chrome": (.systemYellow, "Chrome"),
    "Vivaldi": (.systemRed, "Vivaldi"),
    "Finder": (.systemGray, "Finder"),
    "Mail": (.systemBlue, "Mail"),
    "Messages": (.systemGreen, "Messages"),
    "Slack": (.systemPurple, "Slack"),
    "Discord": (.systemIndigo, "Discord"),
    "Spotify": (.systemGreen, "Spotify"),
    "Music": (.systemPink, "Music"),
    "Photos": (.systemYellow, "Photos"),
    "Notes": (.systemYellow, "Notes"),
    "Reminders": (.systemOrange, "Reminders"),
    "Calendar": (.systemRed, "Calendar"),
    "Maps": (.systemGreen, "Maps"),
    "System Settings": (.systemGray, "Settings"),
    "Activity Monitor": (.systemGreen, "Activity"),
    "Console": (.systemGray, "Console"),
    "Telegram": (.systemBlue, "Telegram"),
]

// Default apps if no config exists
let defaultApps = ["Safari", "Visual Studio Code", "Terminal", "Messages", "Mail"]
var instanceColorOverrides: [String: NSColor] = [:]
var instanceLabelOverrides: [String: String] = [:]

// Config cache: avoid re-reading JSON on every trigger
var cachedAppNames: [String]?
var cachedConfigMtime: Date?
var hasCachedAppNames = false

func configFileMtime() -> Date? {
    let paths = getConfigPaths()
    for path in paths {
        if let attrs = try? FileManager.default.attributesOfItem(atPath: path),
           let mtime = attrs[.modificationDate] as? Date {
            return mtime
        }
    }
    return nil
}

func loadConfigCached(currentMtime mtime: Date? = configFileMtime()) -> [String] {
    if hasCachedAppNames, let cached = cachedAppNames, mtime == cachedConfigMtime {
        return cached
    }
    let result = loadConfig()
    cachedAppNames = result
    cachedConfigMtime = mtime
    hasCachedAppNames = true
    return result
}

func canonicalAppName(_ appName: String) -> String {
    if appName.contains("/") {
        return URL(fileURLWithPath: appName).deletingPathExtension().lastPathComponent
    }
    if appName.hasSuffix(".app") {
        return String(appName.dropLast(4))
    }
    return appName
}

func normalizedAppKey(_ appName: String) -> String {
    canonicalAppName(appName).lowercased()
}

func parseHexColor(_ spec: String) -> NSColor? {
    let trimmed = spec.trimmingCharacters(in: .whitespacesAndNewlines)
    let hex = trimmed.hasPrefix("#") ? String(trimmed.dropFirst()) : trimmed
    guard !hex.isEmpty else { return nil }

    let expanded: String
    if hex.count == 3 {
        expanded = hex.map { "\($0)\($0)" }.joined()
    } else if hex.count == 6 || hex.count == 8 {
        expanded = hex
    } else {
        return nil
    }

    guard let value = UInt64(expanded, radix: 16) else { return nil }

    if expanded.count == 6 {
        let r = CGFloat((value >> 16) & 0xff) / 255.0
        let g = CGFloat((value >> 8) & 0xff) / 255.0
        let b = CGFloat(value & 0xff) / 255.0
        return NSColor(calibratedRed: r, green: g, blue: b, alpha: 1.0)
    }

    let r = CGFloat((value >> 24) & 0xff) / 255.0
    let g = CGFloat((value >> 16) & 0xff) / 255.0
    let b = CGFloat((value >> 8) & 0xff) / 255.0
    let a = CGFloat(value & 0xff) / 255.0
    return NSColor(calibratedRed: r, green: g, blue: b, alpha: a)
}

func parseColorSpec(_ spec: String) -> NSColor? {
    if let hexColor = parseHexColor(spec) {
        return hexColor
    }

    let key = spec
        .trimmingCharacters(in: .whitespacesAndNewlines)
        .lowercased()
        .replacingOccurrences(of: " ", with: "")
        .replacingOccurrences(of: "-", with: "")
        .replacingOccurrences(of: "_", with: "")

    let named: [String: NSColor] = [
        "red": .systemRed,
        "orange": .systemOrange,
        "yellow": .systemYellow,
        "green": .systemGreen,
        "mint": .systemMint,
        "teal": .systemTeal,
        "cyan": .systemCyan,
        "blue": .systemBlue,
        "indigo": .systemIndigo,
        "purple": .systemPurple,
        "pink": .systemPink,
        "brown": .brown,
        "white": .white,
        "black": .black,
        "gray": .systemGray,
        "grey": .systemGray,
        "systemred": .systemRed,
        "systemorange": .systemOrange,
        "systemyellow": .systemYellow,
        "systemgreen": .systemGreen,
        "systemmint": .systemMint,
        "systemteal": .systemTeal,
        "systemcyan": .systemCyan,
        "systemblue": .systemBlue,
        "systemindigo": .systemIndigo,
        "systempurple": .systemPurple,
        "systempink": .systemPink,
        "systemgray": .systemGray,
        "systemgrey": .systemGray,
    ]

    return named[key]
}

func appHomeDirectory() -> String {
    if let envHome = ProcessInfo.processInfo.environment["PISWITCH_HOME"], !envHome.isEmpty {
        return envHome
    }

    let execURL = URL(fileURLWithPath: CommandLine.arguments[0]).standardizedFileURL.resolvingSymlinksInPath()
    let binDir = execURL.deletingLastPathComponent()
    if binDir.lastPathComponent == "bin" {
        let distDir = binDir.deletingLastPathComponent()
        if distDir.lastPathComponent == "dist" {
            return distDir.deletingLastPathComponent().path
        }
    }

    return FileManager.default.currentDirectoryPath
}

func appConfigDir() -> String {
    URL(fileURLWithPath: appHomeDirectory()).appendingPathComponent("config/instances").path
}

func appRunDir() -> String {
    URL(fileURLWithPath: appHomeDirectory()).appendingPathComponent("run").path
}

func ensureDirectoryExists(_ path: String) {
    try? FileManager.default.createDirectory(atPath: path, withIntermediateDirectories: true)
}

func bootstrapLog(_ message: String) {
    guard debugLoggingEnabled else { return }
    let runDir = appRunDir()
    ensureDirectoryExists(runDir)
    let path = "\(runDir)/piswitch-bootstrap.log"
    let ts = ISO8601DateFormatter().string(from: Date())
    let line = "\(ts) pid=\(ProcessInfo.processInfo.processIdentifier) \(message)\n"
    if let data = line.data(using: .utf8) {
        if FileManager.default.fileExists(atPath: path),
           let handle = FileHandle(forWritingAtPath: path) {
            handle.seekToEndOfFile()
            try? handle.write(contentsOf: data)
            try? handle.close()
        } else {
            try? data.write(to: URL(fileURLWithPath: path), options: .atomic)
        }
    }
}

func logEvent(_ message: String) {
    guard debugLoggingEnabled else { return }
    let runDir = appRunDir()
    ensureDirectoryExists(runDir)
    let path = "\(runDir)/piswitch-events.log"
    let ts = ISO8601DateFormatter().string(from: Date())
    let line = "\(ts) instance=\(instanceName) \(message)\n"
    if let data = line.data(using: .utf8) {
        if FileManager.default.fileExists(atPath: path),
           let handle = FileHandle(forWritingAtPath: path) {
            handle.seekToEndOfFile()
            try? handle.write(contentsOf: data)
            try? handle.close()
        } else {
            try? data.write(to: URL(fileURLWithPath: path), options: .atomic)
        }
    }
}

let applicationSearchRoots = [
    "/Applications",
    "/Applications/Utilities",
    "/System/Applications",
    "/System/Applications/Utilities",
    URL(fileURLWithPath: NSHomeDirectory()).appendingPathComponent("Applications").path,
]

func resolveAppPath(_ appName: String) -> String? {
    if appName.hasPrefix("/") && FileManager.default.fileExists(atPath: appName) {
        return appName
    }

    let relativePath = URL(fileURLWithPath: appHomeDirectory()).appendingPathComponent(appName).path
    if FileManager.default.fileExists(atPath: relativePath) {
        return relativePath
    }

    let appHome = URL(fileURLWithPath: appHomeDirectory())
    let normalizedGroup = URL(fileURLWithPath: appName).deletingPathExtension().lastPathComponent
    if !normalizedGroup.isEmpty {
        let bundledGroupPath = appHome
            .appendingPathComponent("assets/finder-groups")
            .appendingPathComponent("\(normalizedGroup).app").path
        if FileManager.default.fileExists(atPath: bundledGroupPath) {
            return bundledGroupPath
        }

        let legacyGroupPath = appHome
            .deletingLastPathComponent()
            .appendingPathComponent("bin/finder-groups")
            .appendingPathComponent("\(normalizedGroup).app").path
        if FileManager.default.fileExists(atPath: legacyGroupPath) {
            return legacyGroupPath
        }
    }

    // Installed bundle whose filename differs from the configured name, e.g. a
    // config entry of "Telegram" against a "Telegram Desktop.app" install.
    // `open -a` matches the bundle filename only, so resolve to a path instead.
    let baseName = canonicalAppName(appName)
    if !baseName.isEmpty {
        var prefixMatch: String?
        for root in applicationSearchRoots {
            let rootURL = URL(fileURLWithPath: root)
            let exactPath = rootURL.appendingPathComponent("\(baseName).app").path
            if FileManager.default.fileExists(atPath: exactPath) {
                return exactPath
            }
            if prefixMatch == nil,
               let entries = try? FileManager.default.contentsOfDirectory(atPath: root),
               let match = entries.sorted().first(where: {
                   $0.hasSuffix(".app") && $0.hasPrefix("\(baseName) ")
               }) {
                prefixMatch = rootURL.appendingPathComponent(match).path
            }
        }
        // Only after every root has been checked, so an exact match always wins.
        if let prefixMatch = prefixMatch {
            return prefixMatch
        }
    }

    return nil
}

// MARK: - Load Config
func getConfigPaths() -> [String] {
    let configDir = appConfigDir()
    let legacyBase = URL(fileURLWithPath: appHomeDirectory()).deletingLastPathComponent().path
    if instanceName == "default" {
        return [
            "\(configDir)/default.json",
            "\(configDir)/config.json",
            "\(legacyBase)/pie-switcher-config.json",
        ]
    }

    return [
        "\(configDir)/\(instanceName).json",
        "\(configDir)/config-\(instanceName).json",
        "\(legacyBase)/pie-switcher-config-\(instanceName).json",
    ]
}

func loadConfig() -> [String] {
    let configPaths = getConfigPaths()

    instanceColorOverrides = [:]
    instanceLabelOverrides = [:]

    for path in configPaths {
        if let data = try? Data(contentsOf: URL(fileURLWithPath: path)),
           let config = try? JSONDecoder().decode(PieSwitcherConfig.self, from: data) {
            if let labels = config.labels {
                for (appName, label) in labels {
                    let key = normalizedAppKey(appName)
                    if !key.isEmpty {
                        instanceLabelOverrides[key] = label
                    }
                }
            }

            if let colors = config.colors {
                for (appName, spec) in colors {
                    let key = normalizedAppKey(appName)
                    guard !key.isEmpty, let color = parseColorSpec(spec) else { continue }
                    instanceColorOverrides[key] = color
                }
            }

            let count = config.apps.count
            if count < 2 {
                return defaultApps
            } else if count > 8 {
                return Array(config.apps.prefix(8))
            }
            return config.apps
        }
    }

    return defaultApps
}

// MARK: - App Configuration
struct AppConfig {
    let name: String
    let displayName: String
    let number: Int
    let color: NSColor
    let startAngle: Double
    let endAngle: Double
}

var cachedAppConfigs: [AppConfig]?
var cachedAppConfigsMtime: Date?
var hasCachedAppConfigs = false
var cachedAppConfigsVersion = 0

func loadAppConfigsCached() -> [AppConfig] {
    let mtime = configFileMtime()
    if hasCachedAppConfigs, let cached = cachedAppConfigs, mtime == cachedAppConfigsMtime {
        return cached
    }

    let appNames = loadConfigCached(currentMtime: mtime)
    let result = createAppConfigs(appNames: appNames)
    cachedAppConfigs = result
    cachedAppConfigsMtime = mtime
    hasCachedAppConfigs = true
    cachedAppConfigsVersion += 1
    return result
}

func calculateSliceAngles(count: Int) -> [(start: Double, end: Double)] {
    guard count > 0 else { return [] }

    let sliceSize = 360.0 / Double(count)
    var angles: [(start: Double, end: Double)] = []

    for i in 0..<count {
        // Start from top (90°) and go clockwise
        let midAngle = 90.0 - Double(i) * sliceSize
        let halfSlice = sliceSize / 2.0
        let start = midAngle - halfSlice
        let end = midAngle + halfSlice
        angles.append((start: start, end: end))
    }

    return angles
}

func displayNameForApp(_ appName: String) -> String {
    if let override = instanceLabelOverrides[normalizedAppKey(appName)] {
        return override
    }

    if let mapped = defaultAppConfigs[appName] {
        return mapped.displayName
    }

    let baseName = canonicalAppName(appName)

    if let mapped = defaultAppConfigs[baseName] {
        return mapped.displayName
    }

    return baseName
}

func colorForApp(_ appName: String) -> NSColor {
    if let override = instanceColorOverrides[normalizedAppKey(appName)] {
        return override
    }

    if let mapped = defaultAppConfigs[appName] {
        return mapped.color
    }

    let baseName = canonicalAppName(appName)
    if let mapped = defaultAppConfigs[baseName] {
        return mapped.color
    }

    return .systemGray
}

func createAppConfigs(appNames: [String]) -> [AppConfig] {
    let count = appNames.count
    guard count > 0 else { return [] }

    let sliceAngles = calculateSliceAngles(count: count)

    return appNames.enumerated().map { index, name in
        let angles = sliceAngles[index]
        return AppConfig(
            name: name,
            displayName: displayNameForApp(name),
            number: index + 1,
            color: colorForApp(name),
            startAngle: angles.start,
            endAngle: angles.end
        )
    }
}

// MARK: - Pie Menu View
class PieMenuView: NSView {
    var onSelect: ((Int) -> Void)?
    var onCancel: (() -> Void)?
    var sliceLayers: [CAShapeLayer] = []
    var currentIndex: Int? = nil
    let innerRadius: CGFloat = 15
    let outerRadius: CGFloat = 100
    let apps: [AppConfig]

    init(frame frameRect: NSRect, apps: [AppConfig]) {
        self.apps = apps
        super.init(frame: frameRect)
        setup()
    }

    required init?(coder: NSCoder) {
        self.apps = []
        super.init(coder: coder)
    }

    func setup() {
        wantsLayer = true
        layer?.backgroundColor = NSColor.clear.cgColor

        // Create pie slices
        for (i, app) in apps.enumerated() {
            let slice = createSlice(app: app, index: i)
            layer?.addSublayer(slice)
            sliceLayers.append(slice)
        }

        // Add labels
        for (i, app) in apps.enumerated() {
            let label = createLabel(app: app, index: i)
            addSubview(label)
        }

        // Center hole
        let centerHole = CALayer()
        centerHole.frame = CGRect(x: 200 - innerRadius, y: 200 - innerRadius,
                                  width: innerRadius * 2, height: innerRadius * 2)
        centerHole.backgroundColor = NSColor.black.withAlphaComponent(0.5).cgColor
        centerHole.cornerRadius = innerRadius
        layer?.addSublayer(centerHole)

        // Center dot
        let centerDot = CALayer()
        centerDot.frame = CGRect(x: 197, y: 197, width: 6, height: 6)
        centerDot.backgroundColor = NSColor.white.cgColor
        centerDot.cornerRadius = 3
        layer?.addSublayer(centerDot)

        // Tracking
        let tracking = NSTrackingArea(
            rect: bounds,
            options: [.mouseMoved, .mouseEnteredAndExited, .activeAlways],
            owner: self,
            userInfo: nil
        )
        addTrackingArea(tracking)
    }

    func createSlice(app: AppConfig, index: Int) -> CAShapeLayer {
        let slice = CAShapeLayer()
        slice.frame = bounds

        let path = CGMutablePath()
        let center = CGPoint(x: 200, y: 200)
        let startRad = CGFloat(app.startAngle * .pi / 180)
        let endRad = CGFloat(app.endAngle * .pi / 180)

        path.move(to: CGPoint(x: center.x + cos(startRad) * innerRadius,
                              y: center.y + sin(startRad) * innerRadius))
        path.addArc(center: center, radius: innerRadius, startAngle: startRad, endAngle: endRad, clockwise: false)
        path.addArc(center: center, radius: outerRadius, startAngle: endRad, endAngle: startRad, clockwise: true)
        path.closeSubpath()

        slice.path = path
        slice.fillColor = app.color.withAlphaComponent(0.34).cgColor
        slice.strokeColor = NSColor.white.withAlphaComponent(0.1).cgColor
        slice.lineWidth = 1

        return slice
    }

    func createLabel(app: AppConfig, index: Int) -> NSView {
        // Calculate mid angle for label position
        let midAngle = (app.startAngle + app.endAngle) / 2
        let midRad = midAngle * .pi / 180
        let labelRadius = (innerRadius + outerRadius) / 2
        let center = CGPoint(x: 200, y: 200)

        let x = center.x + cos(midRad) * labelRadius
        let y = center.y + sin(midRad) * labelRadius

        let container = NSView(frame: CGRect(x: x - 35, y: y - 30, width: 70, height: 60))

        let numBox = NSView(frame: CGRect(x: 22, y: 35, width: 26, height: 26))
        numBox.wantsLayer = true
        numBox.layer?.backgroundColor = NSColor.white.cgColor
        numBox.layer?.cornerRadius = 13
        container.addSubview(numBox)

        let numLabel = NSTextField(labelWithString: "\(app.number)")
        numLabel.font = .systemFont(ofSize: 14, weight: .bold)
        numLabel.textColor = NSColor.black
        numLabel.alignment = .center
        numLabel.frame = numBox.bounds
        numLabel.backgroundColor = .clear
        numLabel.isBordered = false
        numBox.addSubview(numLabel)

        let nameLabel = NSTextField(frame: CGRect(x: 0, y: 5, width: 70, height: 30))
        let shadow = NSShadow()
        shadow.shadowColor = NSColor.black.withAlphaComponent(0.8)
        shadow.shadowOffset = NSSize(width: 0, height: -0.5)
        shadow.shadowBlurRadius = 1.5
        let attrs: [NSAttributedString.Key: Any] = [
            .font: NSFont.systemFont(ofSize: 11, weight: .bold),
            .foregroundColor: NSColor.white,
            .shadow: shadow,
        ]
        nameLabel.attributedStringValue = NSAttributedString(string: app.displayName, attributes: attrs)
        nameLabel.alignment = .center
        nameLabel.backgroundColor = .clear
        nameLabel.isBordered = false
        nameLabel.isSelectable = false
        nameLabel.isEditable = false
        nameLabel.lineBreakMode = .byWordWrapping
        nameLabel.maximumNumberOfLines = 2
        container.addSubview(nameLabel)

        return container
    }

    func highlight(_ index: Int?) {
        if currentIndex != index {
            currentIndex = index
            for (i, slice) in sliceLayers.enumerated() {
                let isSelected = (i == index)
                let app = apps[i]
                slice.fillColor = app.color.withAlphaComponent(isSelected ? 1.0 : 0.34).cgColor
                slice.strokeColor = isSelected ? NSColor.white.cgColor : NSColor.white.withAlphaComponent(0.1).cgColor
                slice.lineWidth = isSelected ? 2 : 1
            }
        }
    }

    override func mouseMoved(with event: NSEvent) {
        updateSelection(for: event.locationInWindow)
    }

    override func mouseDragged(with event: NSEvent) {
        updateSelection(for: event.locationInWindow)
    }

    func updateSelection(for loc: CGPoint) {
        let localPoint = convert(loc, from: nil)
        let center = CGPoint(x: 200, y: 200)
        let dx = localPoint.x - center.x
        let dy = localPoint.y - center.y
        let dist = sqrt(dx * dx + dy * dy)

        guard dist > innerRadius && dist < outerRadius + 30 else {
            highlight(nil)
            return
        }

        var foundIndex: Int? = nil
        for (i, slice) in sliceLayers.enumerated() {
            if let path = slice.path,
               path.contains(localPoint, using: .winding, transform: .identity) {
                foundIndex = i
                break
            }
        }

        highlight(foundIndex)
    }

    override func mouseExited(with event: NSEvent) {
        highlight(nil)
    }

    override func mouseUp(with event: NSEvent) {
        if let idx = currentIndex {
            onSelect?(idx)
        } else {
            onCancel?()
        }
    }

    override func mouseDown(with event: NSEvent) {
        updateSelection(for: event.locationInWindow)
    }
}

// MARK: - Pie Menu Window
class PieMenuWindow: NSWindow {
    var onSelect: ((Int) -> Void)?
    var onCancel: (() -> Void)?
    let apps: [AppConfig]

    init(mouseLocation: NSPoint, apps: [AppConfig]) {
        self.apps = apps
        let size: CGFloat = 400

        var targetScreen: NSScreen?
        for screen in NSScreen.screens {
            let frame = screen.frame
            if mouseLocation.x >= frame.minX && mouseLocation.x <= frame.maxX &&
               mouseLocation.y >= frame.minY && mouseLocation.y <= frame.maxY {
                targetScreen = screen
                break
            }
        }

        guard let screen = targetScreen ?? NSScreen.main else {
            super.init(contentRect: NSRect(x: 0, y: 0, width: size, height: size),
                       styleMask: .borderless, backing: .buffered, defer: false)
            return
        }

        let screenFrame = screen.frame
        var winX = mouseLocation.x - size / 2
        var winY = mouseLocation.y - size / 2

        let margin: CGFloat = 60
        if winX < screenFrame.minX + margin {
            winX = screenFrame.minX + margin
        } else if winX + size > screenFrame.maxX - margin {
            winX = screenFrame.maxX - size - margin
        }

        if winY < screenFrame.minY + margin {
            winY = screenFrame.minY + margin
        } else if winY + size > screenFrame.maxY - margin {
            winY = screenFrame.maxY - size - margin
        }

        super.init(contentRect: NSRect(x: winX, y: winY, width: size, height: size),
                   styleMask: .borderless, backing: .buffered, defer: false)

        isReleasedWhenClosed = false
        backgroundColor = .clear
        isOpaque = false
        hasShadow = false
        level = .floating
        ignoresMouseEvents = false

        let view = PieMenuView(frame: NSRect(x: 0, y: 0, width: 400, height: 400), apps: apps)
        view.onSelect = { [weak self] index in self?.onSelect?(index) }
        view.onCancel = { [weak self] in self?.onCancel?() }
        contentView = view
    }

    override func keyDown(with event: NSEvent) {
        let keyCode = event.keyCode
        let chars = event.charactersIgnoringModifiers ?? ""
        let appCount = apps.count

        let numberKeyMap: [UInt16: Int] = [
            18: 0, 83: 0,
            19: 1, 84: 1,
            20: 2, 85: 2,
            21: 3, 86: 3,
            23: 4, 87: 4,
            22: 5, 88: 5,
            26: 6, 89: 6,
            28: 7, 91: 7,
        ]

        if keyCode == 53 {
            onCancel?()
            return
        }

        if let index = numberKeyMap[keyCode], index < appCount {
            onSelect?(index)
            return
        }

        switch chars {
        case "1": if appCount > 0 { onSelect?(0) }
        case "2": if appCount > 1 { onSelect?(1) }
        case "3": if appCount > 2 { onSelect?(2) }
        case "4": if appCount > 3 { onSelect?(3) }
        case "5": if appCount > 4 { onSelect?(4) }
        case "6": if appCount > 5 { onSelect?(5) }
        case "7": if appCount > 6 { onSelect?(6) }
        case "8": if appCount > 7 { onSelect?(7) }
        case "\u{1b}": onCancel?()
        default: super.keyDown(with: event)
        }
    }

    override var canBecomeKey: Bool { true }
    override var canBecomeMain: Bool { true }
}

// MARK: - Instance Management
func parseArguments() {
    let args = CommandLine.arguments
    for i in 0..<args.count {
        if args[i] == "--instance" && i + 1 < args.count {
            instanceName = args[i + 1]
        }
    }
}

func getPIDFilePath() -> String {
    let runDir = appRunDir()
    let ns = namespacePrefix()
    if instanceName == "default" {
        return "\(runDir)/\(ns).pid"
    }
    return "\(runDir)/\(ns)-\(instanceName).pid"
}

func getTriggerPath() -> String {
    let runDir = appRunDir()
    let ns = namespacePrefix()
    if instanceName == "default" {
        return "\(runDir)/\(ns)-trigger"
    }
    return "\(runDir)/\(ns)-trigger-\(instanceName)"
}

// MARK: - PID File
var pidFilePath: String { getPIDFilePath() }

func writePIDFile() {
    let pid = ProcessInfo.processInfo.processIdentifier
    try? "\(pid)".write(toFile: pidFilePath, atomically: true, encoding: .utf8)
}

func cleanupPIDFile() {
    try? FileManager.default.removeItem(atPath: pidFilePath)
}

// MARK: - Single Instance Check
func commandInstanceName(_ command: String) -> String? {
    let tokens = command.split(whereSeparator: \.isWhitespace).map(String.init)
    guard let executable = tokens.first,
          executable.hasSuffix("/piswitch") || executable.hasSuffix("/PiSwitch") else {
        return nil
    }

    if let idx = tokens.firstIndex(of: "--instance"), idx + 1 < tokens.count {
        return tokens[idx + 1]
    }

    return "default"
}

func checkAndKillExisting() {
    bootstrapLog("checkAndKillExisting:start instance=\(instanceName)")
    let currentPID = ProcessInfo.processInfo.processIdentifier
    let pidFilePath = getPIDFilePath()

    if let pidStr = try? String(contentsOfFile: pidFilePath, encoding: .utf8),
       let pid = Int(pidStr.trimmingCharacters(in: .whitespacesAndNewlines)),
       pid != currentPID {
        bootstrapLog("checkAndKillExisting:kill-pidfile pid=\(pid)")
        kill(pid_t(pid), SIGTERM)
        usleep(50_000)
    }
    bootstrapLog("checkAndKillExisting:end")
}

// MARK: - App Delegate
@MainActor
class AppDelegate: NSObject, NSApplicationDelegate {
    static var instance: AppDelegate?
    var window: PieMenuWindow?
    var appConfigs: [AppConfig] = []
    var windowAppConfigsVersion: Int?
    var triggerSource: DispatchSourceFileSystemObject?
    var triggerFD: Int32 = -1
    var signalSource: DispatchSourceSignal?
    var wakeObservers: [NSObjectProtocol] = []
    var globalClickMonitor: Any?
    var isTransitioning = false
    var processActivity: NSObjectProtocol?
    var lastTriggerAt: CFAbsoluteTime = 0

    override init() {
        super.init()
        AppDelegate.instance = self
    }

    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.setActivationPolicy(.accessory)
        ensureDirectoryExists(appRunDir())
        ensureDirectoryExists(appConfigDir())
        logEvent("did-finish-launching")

        // Prevent macOS from killing/napping the idle daemon.
        // Default to allowing idle sleep so the Mac can sleep when locked;
        // set PISWITCH_PREVENT_IDLE_SLEEP=1 to revert to legacy system-sleep blocking.
        ProcessInfo.processInfo.disableSuddenTermination()
        ProcessInfo.processInfo.disableAutomaticTermination("piswitch daemon")
        let activityOptions: ProcessInfo.ActivityOptions = preventIdleSleep
            ? .userInitiated
            : .userInitiatedAllowingIdleSystemSleep
        processActivity = ProcessInfo.processInfo.beginActivity(
            options: activityOptions,
            reason: "PiSwitch daemon waiting for activation"
        )

        writePIDFile()
        setupTriggerWatch()
        setupSignalWatch()
        setupWakeNotifications()

        // Show menu immediately on first launch
        showMenu()
    }

    func setupTriggerWatch() {
        ensureDirectoryExists(appRunDir())
        let triggerPath = getTriggerPath()
        FileManager.default.createFile(atPath: triggerPath, contents: nil)
        let fd = open(triggerPath, O_EVTONLY)
        guard fd >= 0 else { return }
        triggerFD = fd
        logEvent("watch-start trigger=\(triggerPath)")

        let source = DispatchSource.makeFileSystemObjectSource(
            fileDescriptor: fd,
            eventMask: [.write],
            queue: .main
        )
        source.setEventHandler { [weak self] in
            guard let self else { return }
            let now = CFAbsoluteTimeGetCurrent()
            if triggerDebounceEnabled && now - self.lastTriggerAt < triggerDebounceInterval {
                logEvent("watch-fired:ignored-duplicate")
                return
            }
            self.lastTriggerAt = now
            logEvent("watch-fired")
            self.showMenu()
        }
        source.setCancelHandler {
            close(fd)
        }
        source.resume()
        triggerSource = source
    }

    func setupSignalWatch() {
        signal(SIGUSR1, SIG_IGN)
        let source = DispatchSource.makeSignalSource(signal: SIGUSR1, queue: .main)
        source.setEventHandler { [weak self] in
            guard let self else { return }
            let now = CFAbsoluteTimeGetCurrent()
            if triggerDebounceEnabled && now - self.lastTriggerAt < triggerDebounceInterval {
                logEvent("signal-fired:ignored-duplicate")
                return
            }
            self.lastTriggerAt = now
            logEvent("signal-fired")
            self.showMenu()
        }
        source.resume()
        signalSource = source
    }

    func setupWakeNotifications() {
        let wsCenter = NSWorkspace.shared.notificationCenter
        let wakeObserver = wsCenter.addObserver(
            forName: NSWorkspace.didWakeNotification,
            object: nil,
            queue: .main
        ) { [weak self] _ in
            Task { @MainActor [weak self] in
                logEvent("workspace-did-wake")
                self?.rearmTriggerWatch()
            }
        }
        let screensWakeObserver = wsCenter.addObserver(
            forName: NSWorkspace.screensDidWakeNotification,
            object: nil,
            queue: .main
        ) { [weak self] _ in
            Task { @MainActor [weak self] in
                logEvent("workspace-screens-did-wake")
                self?.rearmTriggerWatch()
            }
        }
        let screenParamsObserver = NotificationCenter.default.addObserver(
            forName: NSApplication.didChangeScreenParametersNotification,
            object: nil,
            queue: .main
        ) { [weak self] _ in
            Task { @MainActor [weak self] in
                logEvent("screen-parameters-changed")
                self?.discardWindow()
            }
        }
        wakeObservers = [wakeObserver, screensWakeObserver, screenParamsObserver]
    }

    func rearmTriggerWatch() {
        logEvent("rearm-trigger-watch")
        discardWindow()
        triggerSource?.cancel()
        triggerSource = nil
        if triggerFD >= 0 {
            close(triggerFD)
            triggerFD = -1
        }
        setupTriggerWatch()
    }

    func positionWindow(_ window: NSWindow, at mouseLoc: NSPoint) {
        let size = window.frame.size
        let newOrigin = NSPoint(x: mouseLoc.x - size.width / 2,
                                y: mouseLoc.y - size.height / 2)
        window.setFrameOrigin(newOrigin)
    }

    func installGlobalClickMonitor() {
        if let monitor = globalClickMonitor {
            NSEvent.removeMonitor(monitor)
        }
        globalClickMonitor = NSEvent.addGlobalMonitorForEvents(matching: [.leftMouseDown, .rightMouseDown]) { [weak self] _ in
            guard let self, let window = self.window else { return }
            let mouseLoc = NSEvent.mouseLocation
            if !window.frame.contains(mouseLoc) {
                self.hideMenu()
            }
        }
    }

    func discardWindow() {
        window?.close()
        window = nil
        windowAppConfigsVersion = nil
    }

    func showMenu() {
        // If menu is already showing, just reposition to current mouse
        if let existingWindow = window, existingWindow.isVisible {
            let mouseLoc = NSEvent.mouseLocation
            positionWindow(existingWindow, at: mouseLoc)
            existingWindow.makeKeyAndOrderFront(nil)
            NSApp.activate(ignoringOtherApps: true)
            if let view = existingWindow.contentView as? PieMenuView {
                view.highlight(nil)
            }
            logEvent("show-menu:reposition")
            return
        }

        isTransitioning = true

        appConfigs = loadAppConfigsCached()
        logEvent("show-menu apps=\(appConfigs.count)")

        let mouseLoc = NSEvent.mouseLocation
        if !windowReuseEnabled || window == nil || windowAppConfigsVersion != cachedAppConfigsVersion {
            discardWindow()
            window = PieMenuWindow(mouseLocation: mouseLoc, apps: appConfigs)
            windowAppConfigsVersion = cachedAppConfigsVersion

            window?.onSelect = { [weak self] index in
                self?.launchApp(index)
            }
            window?.onCancel = { [weak self] in
                self?.hideMenu()
            }
        } else if let existingWindow = window {
            positionWindow(existingWindow, at: mouseLoc)
            if let view = existingWindow.contentView as? PieMenuView {
                view.highlight(nil)
            }
        }

        window?.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)
        logEvent("menu-visible")

        installGlobalClickMonitor()

        isTransitioning = false
    }

    func hideMenu() {
        if let monitor = globalClickMonitor {
            NSEvent.removeMonitor(monitor)
            globalClickMonitor = nil
        }
        if windowReuseEnabled {
            window?.orderOut(nil)
        } else {
            discardWindow()
        }
        logEvent("menu-hidden")
    }

    func launchApp(_ index: Int) {
        guard index < appConfigs.count else { return }
        let appName = appConfigs[index].name
        logEvent("launch-app name=\(appName)")

        let task = Process()
        task.launchPath = "/usr/bin/open"
        if let appPath = resolveAppPath(appName) {
            task.arguments = [appPath]
        } else {
            task.arguments = ["-a", appName]
        }
        try? task.run()
        hideMenu()
    }

    func applicationDidResignActive(_ notification: Notification) {
        guard !isTransitioning else { return }
        hideMenu()
    }

    func applicationWillTerminate(_ notification: Notification) {
        if let monitor = globalClickMonitor {
            NSEvent.removeMonitor(monitor)
            globalClickMonitor = nil
        }
        for observer in wakeObservers {
            NotificationCenter.default.removeObserver(observer)
            NSWorkspace.shared.notificationCenter.removeObserver(observer)
        }
        wakeObservers.removeAll()
        signalSource?.cancel()
        signalSource = nil
        triggerSource?.cancel()
        triggerSource = nil
        if triggerFD >= 0 {
            close(triggerFD)
            triggerFD = -1
        }
        discardWindow()
        cleanupPIDFile()
        try? FileManager.default.removeItem(atPath: getTriggerPath())
    }

    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool {
        return false
    }
}

// MARK: - Main
@main
struct PiSwitch {
    static func main() {
        bootstrapLog("main:start")
        parseArguments()
        bootstrapLog("main:parsed instance=\(instanceName)")
        checkAndKillExisting()
        bootstrapLog("main:after-check")
        let app = NSApplication.shared
        let delegate = AppDelegate()
        app.delegate = delegate
        bootstrapLog("main:before-run")
        app.run()
    }
}
