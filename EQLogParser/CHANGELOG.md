# THJLogParser Changelog

## Version 1.0.1.0 - May 24, 2025

### New Features

- **Auto-load Recent Log**: Parser now automatically loads your most recently-opened log file when started
- **In-game Damage Meter**: Damage meter overlay now functions including configuration (available via View -> Damage Meter menu)
- **BETA: Gina Triggers**: Trigger functionality now available via View -> Triggers menu. This functionality is very much in BETA stage due to its complexity

## Version 1.0.0.0

### New Features

- **One-click Screenshot**: Fast screenshots with frames via the "Copy Parse to Clipboard" button on Breakdown views
- **Pet Tracking Improvement**: Now correctly tracks and includes "Pets of Pets" (such as swarm proc weapons)
- **Additional Data Column**: New "Min Damage" column option available in damage breakdowns to show minimum hit values

### Performance Improvements

- **Optimized Log Processing**: Significantly faster parsing and processing of log files
- **Enhanced DPS Calculation**: More accurate DPS calculations based on actual damage-per-encounter-time

### Fixes & Improvements

- **Spell Classification**: Spells now correctly categorized as Direct Damage (DD) vs Damage over Time (DoT) based on the game's spell dictionary
