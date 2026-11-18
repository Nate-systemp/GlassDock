---
type: "query"
date: "2026-11-18T14:11:03.262949+00:00"
question: "Why does adjusting sound or brightness in fullscreen reveal the main dock and utilities?"
contributor: "graphify"
outcome: "useful"
source_nodes: ["WindowsOverlayManager", "FullscreenPolicy"]
---

# Q: Why does adjusting sound or brightness in fullscreen reveal the main dock and utilities?

## Answer

Expanded query from graph vocabulary: fullscreen foreground overlay policy topmost volume brightness. WindowsOverlayManager.cs L53 RefreshFullscreenPolicy and L223 EnsureTopmost are the native suppression entry points. FullscreenPolicy.cs L3 owns the geometry policy. Source inspection was required after graph retrieval; graph snapshot predates the fix.

## Outcome

- Signal: useful

## Source Nodes

- WindowsOverlayManager
- FullscreenPolicy