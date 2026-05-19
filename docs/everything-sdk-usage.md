# Everything SDK Usage Guide

## Overview

FileSyncTracker uses the Everything SDK (Everything64.dll) for fast file searching and tracking.

## Requirements

1. Install Everything from https://www.voidtools.com/
2. Ensure Everything64.dll is in the configured path (default: `C:\Program Files\Everything\Everything64.dll`)

## P/Invoke Functions

### Search Functions

```csharp
// Set search query
Everything_SetSearchW(string lpSearchString);

// Execute query (bWait = true to wait for results)
Everything_QueryW(bool bWait);

// Get number of results
Everything_GetNumResults();

// Get full path of result at index
Everything_GetResultFullPathNameW(uint nIndex, StringBuilder lpString, uint nMaxCount);

// Get file size of result
Everything_GetResultSize(uint nIndex, out long lpFileSize);

// Get last modified time of result
Everything_GetResultDateModified(uint nIndex, out long lpFileTime);

// Reset search state
Everything_Reset();
```

## File Identity Matching Strategy

When tracking a moved file, the service uses this priority:

1. **NTFS FileId Match** - Most reliable for same-disk moves
2. **Full Identity Match** - FileName + FileSize + LastModified
3. **Fallback Match** - FileName + FileSize only

## Thread Safety

All Everything SDK calls must be executed on an STA thread. The service wraps calls in `Task.Run()` with appropriate thread management.

## Error Handling

- If Everything is not running, the service returns null gracefully
- API errors are logged but don't crash the application
- Retry logic handles temporary unavailability
