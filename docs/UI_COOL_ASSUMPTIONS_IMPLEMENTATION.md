# UI Implementation: Cool Data Assumptions Configuration System

## Overview
Completed the UI implementation for the Cool Data Assumptions Configuration System, enabling users to configure default, job-specific, and volume-specific assumptions for cool data percentage and retrieval percentage when actual metrics are unavailable for ANF volumes with cool access enabled.

## Implementation Details

### 1. Settings Page (`src/frontend/settings.html`)
**Location**: New "Cool Data Assumptions" card section added after Model Preference section

**Features**:
- **Global Defaults Configuration**:
  - Cool Data Percentage input (0-100%, default: 80%)
  - Cool Data Retrieval Percentage input (0-100%, default: 15%)
  - Save Defaults button - saves global assumptions
  - Reset to Factory button - resets fields to 80% and 15%
  
**JavaScript Functions** (added to settings.html):
- `loadCoolAssumptions()`: Loads global defaults from API on page load
- `saveCoolAssumptions()`: Validates and saves global defaults to API
- `resetCoolAssumptions()`: Resets fields to factory defaults (80%, 15%)

**API Endpoints Used**:
- `GET /api/cool-assumptions/global` - Load current global defaults
- `PUT /api/cool-assumptions/global` - Save new global defaults

### 2. Job Detail Page (`src/frontend/job-detail.html`)
**Location**: New "Job Settings" button added to action bar, opens modal dialog

**Features**:
- **Job Settings Button**: New button with gear icon (⚙️) in job header
- **Job Settings Modal**:
  - Full-screen modal overlay with centered dialog
  - Cool Data Percentage input (job-wide)
  - Cool Data Retrieval Percentage input (job-wide)
  - Save Job Settings button - saves and triggers recalculation of all cool volumes
  - Clear Overrides button - reverts to global defaults and recalculates
  - Cancel button - closes modal without saving
  - Status indicator showing current source (Global/Job)

**JavaScript Functions** (added to `src/frontend/js/job-detail-unified.js`):
- `openJobSettings()`: Opens modal and loads current job assumptions from API
- `closeJobSettings()`: Closes the modal
- `saveJobCoolAssumptions()`: Validates inputs, saves to API, triggers recalculation, reloads volumes
- `clearJobCoolAssumptions()`: Deletes job-level overrides, triggers recalculation, reloads volumes

**API Endpoints Used**:
- `GET /api/cool-assumptions/job/{jobId}` - Load job assumptions with source
- `PUT /api/cool-assumptions/job/{jobId}` - Save job assumptions and recalculate
- `DELETE /api/cool-assumptions/job/{jobId}` - Clear job overrides and recalculate

**User Experience**:
- Modal displays current assumptions and their source (Global or Job)
- Status indicator changes color based on source:
  - Green: Job-wide overrides active
  - Grey: Using global defaults
- Saving triggers automatic cost recalculation for all affected volumes
- Volume table refreshes to show updated costs

### 3. Volume Detail Page (`src/frontend/volume-detail.html`)
**Location**: New "Cool Data Assumptions" section added between Historical Metrics and ANF Sizing sections

**Features**:
- **Conditional Display**: Section only appears for ANF volumes with `CoolAccessEnabled === true`
- **Volume-Specific Configuration**:
  - Cool Data Percentage input
  - Cool Data Retrieval Percentage input
  - Save & Recalculate button - saves and recalculates this volume's cost
  - Clear Override button - removes volume override and recalculates
  - Status indicator showing assumptions source and whether metrics are available

**JavaScript Functions** (added to `src/frontend/js/volume-detail.js`):
- `loadCoolAssumptions()`: 
  - Checks if volume is ANF with cool access enabled
  - Shows/hides section accordingly
  - Loads current assumptions from API
  - Displays status with color coding based on source
- `saveVolumeCoolAssumptions()`: Validates, saves, triggers recalculation, reloads volume
- `clearVolumeCoolAssumptions()`: Deletes volume override, triggers recalculation, reloads volume

**API Endpoints Used**:
- `GET /api/cool-assumptions/volume/{jobId}/{volumeResourceId}` - Load volume assumptions with source
- `PUT /api/cool-assumptions/volume/{jobId}/{volumeResourceId}` - Save volume assumptions and recalculate
- `DELETE /api/cool-assumptions/volume/{jobId}/{volumeResourceId}` - Clear volume override and recalculate

**Status Indicator Colors**:
- **Green**: "Using actual metrics from monitoring" (HasMetrics = true)
- **Orange**: "Volume-specific override active" (Source = Volume)
- **Blue**: "Using job-wide assumptions" (Source = Job)
- **Grey**: "Using global defaults" (Source = Global)

**User Experience**:
- Section automatically appears/disappears based on volume type and cool access
- Status clearly indicates which level of assumptions is being used
- Metrics always override assumptions when available
- Saving triggers immediate cost recalculation for this volume only

## Hierarchy Implementation

The UI correctly implements the 3-tier hierarchy:

1. **Global Defaults** (Settings page)
   - Applies to all jobs and volumes unless overridden
   - Changing global does NOT affect existing job/volume overrides

2. **Job-Wide Overrides** (Job Settings modal)
   - Applies to all cool volumes in the job
   - Overrides global defaults
   - Does NOT override volume-specific settings
   - Triggers recalculation of all affected volumes when saved/cleared

3. **Volume-Specific Overrides** (Volume Detail page)
   - Highest priority
   - Applies only to the specific volume
   - Overrides both job and global settings
   - Triggers recalculation of only this volume when saved/cleared

4. **Actual Metrics** (Automatic)
   - Highest priority when available
   - Non-zero metrics from monitoring always override assumptions
   - Status indicator shows "Using actual metrics from monitoring"

## Input Validation

All three UI levels implement consistent validation:
- Cool Data Percentage: 0-100%
- Cool Data Retrieval Percentage: 0-100%
- Type: number with 0.1 step for decimal precision
- Toast error messages for out-of-range values

## User Feedback

### Success Messages
- "Cool data assumptions saved successfully" (global)
- "Job cool assumptions saved and costs recalculated" (job)
- "Volume cool assumptions saved and cost recalculated" (volume)
- "Job overrides cleared and costs recalculated"
- "Volume overrides cleared and cost recalculated"

### Error Messages
- "Cool data percentage must be between 0 and 100"
- "Cool data retrieval percentage must be between 0 and 100"
- API error messages when save/load fails

### Confirmation Dialogs
- Clearing job overrides: "Clear job-wide cool assumptions? This will revert to global defaults and recalculate costs."
- Clearing volume overrides: "Clear volume-specific cool assumptions? This will revert to job or global defaults and recalculate cost."

## Files Modified

### Frontend HTML
1. `src/frontend/settings.html`
   - Added Cool Data Assumptions card with inputs and buttons
   - Added JavaScript functions for load/save/reset

2. `src/frontend/job-detail.html`
   - Added Job Settings button to header
   - Added Job Settings modal with inputs and buttons

3. `src/frontend/volume-detail.html`
   - Added Cool Data Assumptions section (conditionally displayed)

### Frontend JavaScript
1. `src/frontend/js/job-detail-unified.js`
   - Added `openJobSettings()` function
   - Added `closeJobSettings()` function
   - Added `saveJobCoolAssumptions()` function
   - Added `clearJobCoolAssumptions()` function

2. `src/frontend/js/volume-detail.js`
   - Added `loadCoolAssumptions()` function
   - Added `saveVolumeCoolAssumptions()` function
   - Added `clearVolumeCoolAssumptions()` function
   - Updated `renderVolume()` to call `loadCoolAssumptions()`

## Testing Checklist

### Settings Page
- [ ] Global defaults load on page load (80% and 15% if new)
- [ ] Save button saves global defaults successfully
- [ ] Reset button resets to 80% and 15%
- [ ] Validation works for out-of-range values
- [ ] Success toast appears on save

### Job Settings
- [ ] Job Settings button opens modal
- [ ] Modal loads current job assumptions
- [ ] Status indicator shows correct source (Global/Job)
- [ ] Save button saves job assumptions
- [ ] Save triggers cost recalculation for all cool volumes
- [ ] Clear button removes job overrides
- [ ] Clear triggers recalculation
- [ ] Cancel button closes modal without saving
- [ ] Volume costs update after save/clear
- [ ] Changing global does NOT override existing job settings

### Volume Detail
- [ ] Section only appears for ANF volumes with cool access
- [ ] Section hidden for Azure Files and non-cool ANF volumes
- [ ] Current assumptions load correctly
- [ ] Status indicator shows correct source and color
- [ ] "Using actual metrics" shows when metrics available
- [ ] Save button saves volume assumptions
- [ ] Save triggers cost recalculation for this volume
- [ ] Clear button removes volume overrides
- [ ] Clear triggers recalculation
- [ ] Volume cost updates after save/clear
- [ ] Changing job assumptions does NOT override volume settings

### Hierarchy
- [ ] Volume override > Job override > Global default priority works
- [ ] Actual metrics override all assumptions
- [ ] Changing global doesn't affect jobs with overrides
- [ ] Changing job doesn't affect volumes with overrides
- [ ] Clearing volume override reverts to job (if set) or global
- [ ] Clearing job override reverts to global for all volumes without volume overrides

## Deployment

**Commit**: `44387bf` - "Implement UI for Cool Data Assumptions Configuration System"

**Status**: ✅ Pushed to main branch and deploying via GitHub Actions

**Deployment Workflow**: Azure Static Web Apps CI/CD

## Integration with Backend

All UI changes integrate with the existing backend API endpoints:
- `CoolDataAssumptionsFunction.cs` provides all REST endpoints
- `CoolDataAssumptionsService.cs` handles hierarchy resolution
- `CoolDataRecalculationService.cs` handles cost recalculation
- `CostCollectionService.cs` applies assumptions during cost calculation

The UI is the final piece completing the Cool Data Assumptions Configuration System feature.

## Next Steps

1. ✅ Deploy frontend changes (in progress)
2. Test all three UI levels in the deployed environment
3. Test with real ANF volumes with cool access
4. Verify cost calculations reflect assumptions correctly
5. Test edge cases:
   - Volume with metrics (should show "using actual metrics")
   - Volume without metrics (should use assumptions)
   - Changing assumptions and verifying cost updates
   - Hierarchy precedence (volume > job > global)
6. Document user-facing instructions for this feature
