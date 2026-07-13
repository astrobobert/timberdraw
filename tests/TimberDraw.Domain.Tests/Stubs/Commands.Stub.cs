namespace TimberDraw
{
    // TEST STUB. The real TimberDraw.Commands (Commands.cs, repo root) is AutoCAD-bound
    // (IExtensionApplication) and is NOT linked into this project. FrameSpec.cs -- which IS
    // linked -- calls exactly one of its members: Commands.ReadKPParams(). This stub supplies
    // that symbol with FIXED, DOCUMENTED seed values so factory/migration behavior is
    // deterministic under test.
    //
    // NOTE: these are realistic pinned values, NOT the fresh-machine Settings defaults (which
    // are mostly 0 and would run migrations through degenerate zero-size sections). A user
    // with saved Settings gets proportionally different seeded sizes; the tests pin the
    // migration LOGIC, not any particular user's catalog. Field set mirrors what the real
    // ReadKPParams populates (Commands.cs).
    internal static class Commands
    {
        internal static KPBentParams ReadKPParams() => new KPBentParams
        {
            Span = 288.0,
            EaveHt = 144.0,
            Pitch = 8.0 / 12.0,
            PostW = 8, PostD = 8,
            GirtW = 8, GirtD = 10,
            RafterW = 6, RafterD = 8,
            KpostW = 8, KpostD = 8,
            RidgeW = 6, RidgeD = 8,
            BraceW = 4, BraceD = 6, BraceLength = 36, BraceAngle = 45.0, HasBrace = true,
            StrutW = 4, StrutD = 6, HasStrut = false,
            VStrutW = 4, VStrutD = 6, HasVStrut = false,
            StrutAngle = 45.0,
            OffsetType = 0,
            BaySpacings = new[] { 96.0, 144.0 },   // matches the shipped BaySchedule default "96,144"
            UseCommons = true,
            CommonMode = 1, CommonCount = 3, CommonSpacing = 48, CommonW = 3, CommonD = 5,
            PurlinMode = 1, PurlinCount = 2, PurlinSpacing = 48, PurlinW = 4, PurlinD = 5,
            HasFloorGirt = true, HasFloorBrace = true,
            FloorGirtW = 8, FloorGirtD = 10, FloorGirtHt = 72.0,
            GirtDrop = 6.0,
            Make3D = false
        };
    }
}
