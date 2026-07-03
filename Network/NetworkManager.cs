using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;

namespace TimberDraw
{
    // NetworkManager: drives joint geometry from BentNetwork edges.
    //
    // Phase A (current): BentNetwork is written by orchestrators.  NetworkManager
    //   exists but its read methods are not yet wired into TimberFactory.
    //
    // Phase B (next): TimberFactory.ApplyIncomingJointsAndMortises calls
    //   NetworkManager.ReapplyIncoming(newTimberId) to supplement or replace
    //   the per-member IncomingJoints xrecord lookup.  This makes direct regen
    //   consistent for ALL bent types without per-type migration.
    //
    // Phase C (future): TimberFactory.Regenerate calls NetworkManager.UpdateEdge
    //   when a joint type/params changes.  NetworkManager handles the delta-swap
    //   on the receiver instead of ApplyJointTypes + per-member pre-emption.
    //
    // Phase D (future): Member Draw() methods become pure body generators.
    //   NetworkManager.ApplyNetwork(bentNumber) handles all joint geometry after
    //   all bodies are drawn, replacing per-member NearJointType/FarJointType logic.
    public static class NetworkManager
    {
        // -----------------------------------------------------------------------
        // Phase B: Direct regen -- re-cut all incoming joints into freshly drawn body
        // -----------------------------------------------------------------------

        // Re-cuts all incoming JF joints for a freshly drawn receiver timber using
        // edges from BentNetwork (exact geometry, not bboxes).
        //
        // Called from TimberFactory.ApplyIncomingJointsAndMortises alongside the
        // existing _pendingIncomingJoints path.  When BentNetwork has entries, they
        // take precedence; when absent (old drawing), the existing paths are used.
        //
        // Returns the set of giver handles whose edges were successfully re-cut
        // so the caller can skip the corresponding bbox fallback entries.
        public static HashSet<Handle> ReapplyIncoming(ObjectId newTimberId)
        {
            var applied = new HashSet<Handle>();
            if (newTimberId.IsNull || newTimberId.IsErased) return applied;

            Handle h = newTimberId.Handle;
            JointEdge[] incoming = BentNetwork.GetIncomingEdges(h);
            if (incoming.Length == 0) return applied;

            foreach (JointEdge edge in incoming)
            {
                if (string.Equals(edge.JointType, "Butt", StringComparison.OrdinalIgnoreCase))
                {
                    applied.Add(edge.GiverHandle);  // Butt: no geometry, but mark as handled
                    continue;
                }
                if (edge.Params.JointType == Module1.JointType.None) continue;

                // Reconstruct exact mortise solid from stored params and cut into new body.
                var recut = edge.Params;
                recut.GeneratePegs = false;
                Module1.SuppressNextMortiseBbox();
                ObjectId mortise = JointFactory.Create(recut.JointType, recut);
                if (mortise.IsNull) continue;

                try   { Module1.AddJoint(newTimberId, mortise, Module1.Joint.Mortise); }
                catch { }
                try   { Module1.DeleteJoint(mortise); }
                catch { }

                // Refresh IncomingJoints on new entity so future regens are exact.
                // Skip Polygon joints: their geometry lives in BentNetwork (CustomPts cannot
                // round-trip through the fixed-16-value IncomingJoints xrecord format).
                if (edge.Params.JointType != Module1.JointType.Polygon)
                    Module1.SaveIncomingJoint(newTimberId, edge.GiverHandle, edge.Params);
                applied.Add(edge.GiverHandle);
            }
            return applied;
        }

        // -----------------------------------------------------------------------
        // Phase C: Joint type change -- update network edge + delta-swap receiver
        // -----------------------------------------------------------------------

        // Updates the edge for (giverHandle, giverEnd) with new type and params,
        // then performs a delta-swap on the receiver: fill the old void with the
        // old params, cut the new void with the new tenon solid.
        //
        // Called from TimberFactory.Regenerate when jn or jf differs from the
        // currently drawn type, INSTEAD of ApplyJointTypes.
        //
        // Note: Phase C not yet wired into TimberFactory -- stub for future use.
        public static void UpdateEdge(Handle giverHandle, short giverEnd,
            string newJointType, JointParams newParams, ObjectId newTenonId)
        {
            JointEdge old = BentNetwork.FindEdge(giverHandle, giverEnd);
            if (old.GiverHandle == default) return;  // edge not found in network

            ObjectId receiverId = Module1.GetObjectIdFromHandle(old.ReceiverHandle);
            if (!receiverId.IsNull && !receiverId.IsErased)
            {
                // Delta-swap: fill old void, cut new void.
                if (!string.Equals(old.JointType, "Butt", StringComparison.OrdinalIgnoreCase))
                {
                    // Fill old void using stored old params.
                    ObjectId fillId = JointFactory.Create(old.Params.JointType, old.Params);
                    if (!fillId.IsNull)
                    {
                        try   { Module1.AddJoint(receiverId, fillId, Module1.Joint.Fill); }
                        catch { }
                        Module1.EraseEntity(fillId);
                    }
                }
                if (!string.Equals(newJointType, "Butt", StringComparison.OrdinalIgnoreCase) &&
                    !newTenonId.IsNull && !newTenonId.IsErased)
                {
                    try   { Module1.AddJoint(receiverId, newTenonId, Module1.Joint.Mortise); }
                    catch { }
                }
            }

            // Persist updated edge.
            BentNetwork.UpdateEdge(giverHandle, giverEnd, newJointType, newParams);
        }

        // -----------------------------------------------------------------------
        // Phase D: Full network-driven draw -- all joint geometry from edges
        // -----------------------------------------------------------------------

        // Applies all JF joint edges registered for a bent after all member bodies
        // have been drawn.  Called by orchestrator at the end of Draw() instead of
        // per-member NearJointType/FarJointType logic.
        //
        // Note: Phase D not yet implemented -- placeholder for architectural reference.
        public static void ApplyNetwork(string bentNumber)
        {
            throw new NotImplementedException(
                "NetworkManager.ApplyNetwork is Phase D -- not yet implemented. " +
                "Orchestrators still handle joint geometry directly.");
        }
    }
}
