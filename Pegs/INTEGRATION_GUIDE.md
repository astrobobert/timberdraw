# TimberScribe -- Joinery & Peg Standards Integration Guide

## Overview
This integration adds **Timber Framers Guild (TFG) peg sizing and placement standards** to the TimberScribe plugin. It enables automatic validation and documentation of pegged joints during the export process.

The three files in this folder (`PegSpecification.cs`, `TFGPegStandards.cs`, `JoineryExporter.cs`) currently live inside the TimberDraw project under the `TimberFrameSuite.Standards` and `TimberScribe.Export` namespaces. When TimberScribe is built, these files should be extracted into a standalone shared library (`TimberFrameSuite.Standards.dll`) referenced by both TimberDraw and TimberScribe.

## Files to Extract

```
Pegs/
  PegSpecification.cs    -- Peg data model (namespace: TimberFrameSuite.Standards)
  TFGPegStandards.cs     -- TFG standards reference and presets (namespace: TimberFrameSuite.Standards)
  JoineryExporter.cs     -- JSON export and validation (namespace: TimberScribe.Export)
```

Move into:
```
TimberFrameSuite.Standards\   [new shared class library project]
  PegSpecification.cs
  TFGPegStandards.cs

TimberScribePlugin\Export\
  JoineryExporter.cs
```

---

## Integration Steps

### Step 1: Create Shared Class Library
Create `TimberFrameSuite.Standards.csproj` targeting `net48`. Move `PegSpecification.cs` and `TFGPegStandards.cs` into it. Reference this library from both TimberDraw and TimberScribe.

### Step 2: Update TimberMeta Model
Modify `Models/TimberMeta.cs` to include joinery information:

```csharp
public class TimberMeta
{
    public string Id { get; set; }
    public string Description { get; set; }
    public string Species { get; set; }

    // Add joinery support
    public JoineryExporter.JoineryMetadata Joinery { get; set; }

    // ... existing properties ...
}
```

### Step 3: Update TsjWriter for JSON Export
Modify `Export/TsjWriter.cs` to serialize joinery data:

```csharp
// In your SerializeToJson() or similar method:
var jsonData = new
{
    timber = new
    {
        id = timber.Id,
        description = timber.Description,
        species = timber.Species,

        // Include joinery specifications
        joinery = timber.Joinery?.ToJsonDictionary(),

        // ... existing properties ...
    }
};
```

### Step 4: Add Joinery to Command Flow
In `Commands/TSScribeCommand.cs`, populate joinery when processing timbers:

```csharp
// In PHASE 2 -- Process section, after creating TimberMeta:

var meta = new TimberMeta
{
    Id = timberId,
    Description = tag.FriendlyLabel,
    Species = null
};

// Initialize empty joinery metadata
var joineryMeta = new JoineryExporter.JoineryMetadata
{
    TimberID = timberId,
    TFGGuidelinesVersion = "TFEC 2024"
};

meta.Joinery = joineryMeta;

// ... continue with face extraction ...
```

### Step 5: Add Validation Logging

```csharp
// After processing timber faces:
if (meta.Joinery != null)
{
    bool isValid = meta.Joinery.ValidateAll();

    if (!isValid)
    {
        ScribeLog("WARNING Joinery validation issues for " + timberId + ":");
        foreach (var error in meta.Joinery.ValidationErrors)
            ScribeLog("   [" + error.Severity + "] " + error.JointType + ": " + error.Message);
    }
    else
    {
        ScribeLog("OK Joinery specifications validated: " + meta.Joinery.Joints.Count + " joint(s)");
    }
}
```

---

## Usage Examples

### Example 1: Create a Tie Beam Joint Specification
```csharp
var tieBeamJoint = new JointSpecification
{
    JointType = "Mortise & Tenon - Tie Beam to Post",
    Description = "2-inch tenon to 6-inch post housing",
    Pegs = TFGPegStandards.TieBeam2InchTenon  // 1" pegs, 3.5" spacing
};

var metadata = new JoineryExporter.JoineryMetadata { TimberID = "POST_B3" };
metadata.Joints.Add(tieBeamJoint);
metadata.ValidateAll();
```

### Example 2: Calculate Custom Peg Sizing
```csharp
// For a 1.75" tenon:
var pegSpec = TFGPegStandards.GetPresetForTenonThickness(1.75);
// Returns: 0.875" diameter peg, 3.0625" spacing, 2.625" edge spacing
```

### Example 3: Validate and Export
```csharp
var joinery = JoineryExporter.CreateSampleTieBeamJoinery();
bool isValid = joinery.ValidateAll();

string report = JoineryExporter.GenerateJoineryReport(joinery);
ed.WriteMessage(report);

var jsonData = joinery.ToJsonDictionary();
```

---

## TFG Standards Reference

### Peg Sizing Rule
```
Peg Diameter = 1/2 x Tenon Thickness

Examples:
  2.0" tenon  ->  1.0" diameter peg
  1.5" tenon  ->  0.75" diameter peg
  1.0" tenon  ->  0.5" diameter peg
```

### Peg Placement Standards

**Along Beam (into the mortise):**
- First peg: 2" from bearing nose (framing square thickness)
- Additional pegs: 3.5 x peg diameter spacing minimum
- Far-end clearance: 2" from tenon tip (symmetric with near-end setback)
- Peg count: only pegs that fit within [2", tenonLength - 2"] are placed
  - Typical 6-8" members: 1-2 pegs
  - Members 11"+ deep: up to 3 pegs

**Across Face:**
- Offset from tenon shoulder: 2" (standard)
  - With 0.5" housing: 2.5"
  - With 0.75" housing: 2.75"
- Edge spacing: 3 x peg diameter minimum

**General:**
- Minimum: 2 pegs per standard joint (TFG minimum; single-peg joints exist for brace/strut geometry)
- Maximum standard peg diameter: 1.25"
- Pegs justified toward gravity for shrinkage stability

---

## JSON Export Format

```json
{
  "timber": {
    "id": "POST_B3",
    "description": "Main Support Post",
    "joinery": {
      "timber_id": "POST_B3",
      "tfg_standards_version": "TFEC 2024",
      "joint_count": 2,
      "joints": [
        {
          "type": "Mortise & Tenon - Tie Beam",
          "description": "Post to tie beam housing joint",
          "reference": "TFEC: Edge Spacing of Pegs in Mortise and Tenon Joints",
          "pegs": {
            "count": 2,
            "diameter_inches": 1.0,
            "tenon_thickness_inches": 2.0,
            "spacing_inches": 3.5,
            "first_peg_setback_inches": 2.0,
            "edge_spacing_inches": 3.0,
            "shoulder_offset_inches": 2.0,
            "justification": "DownwardGravity",
            "summary": "2x dia.1.0\" pegs @ 3.50\" spacing (tenon: 2.0\", ratio: 0.50x)"
          }
        }
      ],
      "validation_errors": []
    }
  }
}
```

---

## Advanced Features

### Drawbored Joints
```csharp
var drawboredJoint = TFGPegStandards.DrawboredJoint1InchTenon;
// Justification = PegJustification.DrawboredOffset
```

### Custom Presets
Add to `TFGPegStandards.cs`:
```csharp
public static PegSpecification MyCustomJoint => new()
{
    DiameterInches = 1.125,
    TenonThicknessInches = 2.25,
    PegCount = 3,
    // ... other properties
};
```

---

## References

**Timber Framers Guild Publications:**
- Edge Spacing of Pegs in Mortise and Tenon Joints (TFEC Research Report)
- Structural Properties of Pegged Timber Connections as Affected by End Distance
- TFEC Technical Bulletins: http://www.tfguild.org

**Guild Resources:**
- Timber Framers Guild: https://www.tfguild.org
- Timber Frame Engineering Council: https://www.tfguild.org/tfg-engineers

---

**Integration Version:** 1.1
**TFG Standards Version:** TFEC 2024
**Updated:** May 2026 -- peg bounds enforcement, TimberDraw rename, cross-bent mortise queue architecture added to TimberDraw (see CLAUDE.md)
