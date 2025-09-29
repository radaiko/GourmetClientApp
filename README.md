# GourmetClientApp - MAUI Cross-Platform Migration

This project is a cross-platform migration of the original [GourmetClient](https://github.com/patrickl92/GourmetClient) from WPF to .NET MAUI, enabling the food ordering application to run on Windows, macOS, iOS, and Android.

## Overview

The Gourmet Client is a simplified food ordering application for Gourmet (a German food service). This MAUI version maintains the same business logic while providing a modern, cross-platform user interface.

## Architecture

### Core Components Migrated

- **Models**: Complete 1:1 migration of business entities
  - `GourmetMenu` - Menu item representation
  - `GourmetMenuCategory` - Menu categorization
  - `BillingPosition` - Billing and order tracking
  - All supporting model classes

- **Views**: New MAUI-based cross-platform UI
  - `MenuOrderPage` - Main menu browsing and ordering interface
  - Modern XAML with cross-platform compatibility
  - Responsive design for mobile and desktop

- **ViewModels**: MVVM pattern with CommunityToolkit.Mvvm
  - `ViewModelBase` - Updated to use CommunityToolkit.Mvvm
  - Sample data integration for demonstration

### Original Architecture Preserved

The migration preserves the original application's:
- Business logic structure
- Data models (1:1 mapping)
- MVVM pattern
- Service-oriented architecture approach

## Technologies Used

- **.NET 9.0 MAUI** - Cross-platform framework
- **CommunityToolkit.Mvvm** - Modern MVVM implementation
- **HtmlAgilityPack** - Web scraping (from original)
- **Semver** - Version handling (from original)

## Platform Support

- ✅ **Windows** - Desktop application
- ✅ **Android** - Mobile application
- 🔄 **iOS** - Ready for iOS development (requires macOS/Xcode)
- 🔄 **macOS** - Ready for macOS development

## Current Status

### ✅ Completed
- MAUI project structure setup
- Core business models migrated
- Basic UI implementation with sample data
- Cross-platform compilation working
- Modern MVVM pattern implementation

### 🔄 In Progress / Next Steps
- Full network layer integration
- Complete ViewModels migration
- Settings and configuration management
- Auto-update functionality adaptation
- Platform-specific optimizations

## Getting Started

### Prerequisites
- .NET 9.0 SDK
- MAUI workloads installed (`dotnet workload install maui-windows maui-android`)

### Building the Application

```bash
# Clone the repository
git clone https://github.com/radaiko/GourmetClientApp.git
cd GourmetClientApp/GourmetClientApp

# Restore packages
dotnet restore

# Build for Android
dotnet build -f net9.0-android

# Build for Windows (on Windows only)
dotnet build -f net9.0-windows
```

### Running the Application

```bash
# Run on Android Emulator
dotnet build -f net9.0-android -t:Run

# Run on Windows
dotnet run -f net9.0-windows
```

## Key Differences from Original

| Aspect | Original (WPF) | MAUI Migration |
|--------|----------------|----------------|
| Platform | Windows only | Cross-platform (Windows, Android, iOS, macOS) |
| UI Framework | WPF | .NET MAUI |
| MVVM | Custom implementation | CommunityToolkit.Mvvm |
| Target Framework | .NET 9.0 Windows | .NET 9.0 Multi-platform |

## Contributing

This is a migration project to demonstrate MAUI cross-platform capabilities. The original application functionality is preserved while extending platform support.

## License

This project maintains compatibility with the original GourmetClient project structure.