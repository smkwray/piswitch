// swift-tools-version: 5.9
import PackageDescription

let package = Package(
    name: "PiSwitch",
    platforms: [
        .macOS(.v13)
    ],
    products: [
        .executable(name: "PiSwitch", targets: ["PiSwitch"])
    ],
    targets: [
        .executableTarget(
            name: "PiSwitch",
            swiftSettings: [
                .enableExperimentalFeature("StrictConcurrency")
            ]
        )
    ]
)
