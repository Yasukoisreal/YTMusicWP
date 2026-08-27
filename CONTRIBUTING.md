# Contributing to YTMusicWP

First off, thank you for considering contributing to YTMusicWP! It's people like you that make this tool great for the Windows Phone community.

## Where do I go from here?

If you've noticed a bug or have a feature request, please make one! It's best if you check the [Issues](https://github.com/Yasukoisreal/YTMusicWP/issues) tab first to see if it's already being tracked.

## Fork & create a pull request

If you want to contribute code:

1. **Start from an issue.** Every PR needs an accepted issue behind it — open one first so the change is agreed before the code exists.
2. Fork the repository.
3. Create a new branch for your feature or bugfix (`git checkout -b feature-name`).
4. Make your changes in the codebase.
5. Commit your changes with descriptive commit messages.
6. Push to your branch (`git push origin feature-name`).
7. Create a Pull Request (PR) from your branch to our `main` branch.

## AI Policy

AI-*assisted* work is welcome; AI-*driven* work is not:
- A human must have written or personally reviewed **every line** and be able to answer review comments about it.
- **Unattended agent submissions** (PRs fired at this repository by coding agents like Jules, Devin, OpenHands, and friends) without a human shaping and checking the result are **closed automatically** by the triage workflow, on sight, without individual discussion.
- Commits carrying AI co-author trailers (`Co-Authored-By: Claude/Copilot/...`) or "Generated with ..." markers are rejected the same way — squash them out before opening the PR.
- Repeat offenders get blocked from the repository.

This is not hostility toward AI tooling. It is the difference between a contribution someone stands behind and unreviewed output pointed at volunteer maintainers. Review time is the scarcest resource this project has; spending it on machine-generated PRs nobody proofread takes it away from contributors who did the work.

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
