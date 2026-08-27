# Contributing to YTMusicWP

First off, thank you for considering contributing to YTMusicWP! It's people like you that make this tool great for the Windows Phone community.

## Where do I go from here?

If you've noticed a bug or have a feature request, please make one! It's best if you check the [Issues](https://github.com/Yasukoisreal/YTMusicWP/issues) tab first to see if it's already being tracked.

## Fork & create a pull request

If you want to contribute code:

1. Fork the repository.
2. Create a new branch for your feature or bugfix (`git checkout -b feature-name`).
3. Make your changes in the codebase.
4. Commit your changes with descriptive commit messages.
5. Push to your branch (`git push origin feature-name`).
6. Create a Pull Request (PR) from your branch to our `main` branch.

## Setting up the development environment

To build and run YTMusicWP locally, you will need:
- Windows 8.1, 10, or 11
- Visual Studio 2015 (with Windows Phone 8.1 SDK installed)
- MSBuild v14.0

### Build steps:

```powershell
# Clone the repository
git clone https://github.com/Yasukoisreal/YTMusicWP.git
cd YTMusicWP

# Restore NuGet packages
.\nuget.exe restore YTMusicWP.sln

# Build ARM configuration
& "C:\Program Files (x86)\MSBuild\14.0\Bin\MSBuild.exe" YTMusicWP.sln /p:Configuration=Debug /p:Platform=ARM
```

## Guidelines

- **Code Style**: Please match the existing code style in the repository.
- **Keep it light**: Remember that this app targets devices with as little as 512MB RAM. Avoid importing large libraries or writing code that creates memory leaks.

Thank you for contributing!
