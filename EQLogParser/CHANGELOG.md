# THJLogParser Changelog

## Version 1.0.1.0 - May 24, 2025

### New Features

- **Auto-load Recent Log**: Parser now automatically loads your most recently-opened log file when started
- **In-game Damage Meter**: Damage meter overlay now functions including configuration (available via View -> Damage Meter menu)
- **Header Menu Consistency**: Header menu consistency cleaned up
- **TRIGGERS!**: Gina Triggers functionality is now operational via View -> Triggers menu.. Check Wiki for documentation.
- **DPS Meter Overlay Default**: DPS meter overlay now selects [DPS] by default to correct unintuitive usability on first use
- **Trigger Panel Resize**: Added resize sliders to Trigger configuration panel edges

### Performance Improvements

- More performance improvements

### Fixes & Improvements

- Fixed damage breakdown auto-sorting so that it actually auto-sorts by total damage when the panel opens rather than lying to you

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
