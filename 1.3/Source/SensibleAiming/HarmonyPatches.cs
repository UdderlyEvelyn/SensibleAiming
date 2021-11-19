using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.Noise;
using UnityEngine;

namespace SA
{
    [HarmonyPatch(typeof(Verb_LaunchProjectile), "TryCastShot")]
    class HarmonyPatches
    {
        static bool Prefix(ref Verb_LaunchProjectile __instance, ref bool __result, ref LocalTargetInfo ___currentTarget, ref int ___lastShotTick, ref bool ___preventFriendlyFire, ref bool ___canHitNonTargetPawnsNow)
		{
			//Check if target valid.
			if (___currentTarget.HasThing && ___currentTarget.Thing.Map != __instance.caster.Map)
			{
				__result = false;
				return false;
			}
			//Check if projectile valid.
			ThingDef projectile = __instance.Projectile;
			if (projectile == null)
			{
				__result = false;
				return false;
			}
			//Cast ray to target
			ShootLine resultingLine;
			bool flag = __instance.TryFindShootLineFromTo(__instance.caster.Position, ___currentTarget, out resultingLine);
			//Stuff for bursts shooting if lost sight
			if (__instance.verbProps.stopBurstWithoutLos && !flag)
			{
				__result = false;
				return false;
			}
			//Check if equipment sourced shot and update comps if present/necessary
			if (__instance.EquipmentSource != null)
			{
				__instance.EquipmentSource.GetComp<CompChangeableProjectile>()?.Notify_ProjectileLaunched();
				__instance.EquipmentSource.GetComp<CompReloadable>()?.UsedOnce();
			}
			//Check if this is a manned object like a mortar/artillery and set some variables if so
			___lastShotTick = Find.TickManager.TicksGame;
			Thing manningPawn = __instance.caster;
			Thing equipmentSource = __instance.EquipmentSource;
			CompMannable compMannable = __instance.caster.TryGetComp<CompMannable>();
			if (compMannable != null && compMannable.ManningPawn != null)
			{
				manningPawn = compMannable.ManningPawn;
				equipmentSource = __instance.caster;
			}
			Vector3 drawPos = __instance.caster.DrawPos;
			//Spawn the projectile along the line from the caster
			Projectile projectile2 = (Projectile)GenSpawn.Spawn(projectile, resultingLine.Source, __instance.caster.Map);
			//If it's a mortar ForcedMissRadius is different, if the radius is particularly high fire this code off (should not need to mess with what's in here for my purposes), note this returns so if it goes down this path we're out of the code.
			if (__instance.verbProps.ForcedMissRadius > 0.5f)
			{
				float num = VerbUtility.CalculateAdjustedForcedMiss(__instance.verbProps.ForcedMissRadius, ___currentTarget.Cell - __instance.caster.Position);
				if (num > 0.5f)
				{
					int max = GenRadial.NumCellsInRadius(num);
					int num2 = Rand.Range(0, max); //Random chance to miss the cell
					if (num2 > 0) //If we miss the cell
					{
						IntVec3 intVec = ___currentTarget.Cell + GenRadial.RadialPattern[num2];
						//ThrowDebugText("ToRadius");
						//ThrowDebugText("Rad\nDest", intVec);
						ProjectileHitFlags projectileHitFlags = ProjectileHitFlags.NonTargetWorld;
						if (Rand.Chance(0.5f))
						{
							projectileHitFlags = ProjectileHitFlags.All;
						}
						if (!___canHitNonTargetPawnsNow)
						{
							projectileHitFlags &= ~ProjectileHitFlags.NonTargetPawns;
						}
						projectile2.Launch(manningPawn, drawPos, intVec, ___currentTarget, projectileHitFlags, ___preventFriendlyFire, equipmentSource);
						return true;
					}
				}
			}
			ShotReport shotReport = ShotReport.HitReportFor(__instance.caster, __instance, ___currentTarget);
			//Get cover object
			Thing randomCoverToMissInto = shotReport.GetRandomCoverToMissInto();
			ThingDef targetCoverDef = randomCoverToMissInto?.def;
			//If we miss randomly **MAYBE MESS WITH THIS**
			if (!Rand.Chance(shotReport.AimOnTargetChance_IgnoringPosture))
			{
				//Make it miss
				resultingLine.ChangeDestToMissWild(shotReport.AimOnTargetChance_StandardTarget);
				//ThrowDebugText("ToWild" + (canHitNonTargetPawnsNow ? "\nchntp" : ""));
				//ThrowDebugText("Wild\nDest", resultingLine.Dest);
				ProjectileHitFlags projectileHitFlags2 = ProjectileHitFlags.NonTargetWorld;
				//Give it a 50% chance to hit pawns that were not targeted
				if (Rand.Chance(0.5f) && ___canHitNonTargetPawnsNow)
				{
					projectileHitFlags2 |= ProjectileHitFlags.NonTargetPawns;
				}
				//Launch projectile
				projectile2.Launch(manningPawn, drawPos, resultingLine.Dest, ___currentTarget, projectileHitFlags2, ___preventFriendlyFire, equipmentSource, targetCoverDef);
				__result = true;
				return false;
			}
			//If we didn't miss randomly, and we do hit cover
			if (___currentTarget.Thing != null && ___currentTarget.Thing.def.category == ThingCategory.Pawn && !Rand.Chance(shotReport.PassCoverChance))
			{
				//ThrowDebugText("ToCover" + (canHitNonTargetPawnsNow ? "\nchntp" : ""));
				//ThrowDebugText("Cover\nDest", randomCoverToMissInto.Position);
				ProjectileHitFlags projectileHitFlags3 = ProjectileHitFlags.NonTargetWorld;
				if (___canHitNonTargetPawnsNow)
				{
					projectileHitFlags3 |= ProjectileHitFlags.NonTargetPawns;
				}
				projectile2.Launch(manningPawn, drawPos, randomCoverToMissInto, ___currentTarget, projectileHitFlags3, ___preventFriendlyFire, equipmentSource, targetCoverDef);
				__result = true;
				return false;
			}
			//We're finally gonna hit 'em
			ProjectileHitFlags projectileHitFlags4 = ProjectileHitFlags.IntendedTarget;
			if (___canHitNonTargetPawnsNow)
			{
				projectileHitFlags4 |= ProjectileHitFlags.NonTargetPawns;
			}
			//Target doesn't have a Thing or Thing is full (what is fillage? full of what?)
			if (!___currentTarget.HasThing || ___currentTarget.Thing.def.Fillage == FillCategory.Full)
			{
				projectileHitFlags4 |= ProjectileHitFlags.NonTargetWorld;
			}
			//ThrowDebugText("ToHit" + (canHitNonTargetPawnsNow ? "\nchntp" : ""));
			//Target Thing is valid
			if (___currentTarget.Thing != null)
			{
				//Launch and hit the thing
				projectile2.Launch(manningPawn, drawPos, ___currentTarget, ___currentTarget, projectileHitFlags4, ___preventFriendlyFire, equipmentSource, targetCoverDef);
				//ThrowDebugText("Hit\nDest", currentTarget.Cell);
			}
			else //It doesn't have a thing?
			{
				//Launch and hit the.. non-Thing?	
				projectile2.Launch(manningPawn, drawPos, resultingLine.Dest, ___currentTarget, projectileHitFlags4, ___preventFriendlyFire, equipmentSource, targetCoverDef);
				//ThrowDebugText("Hit\nDest", resultingLine.Dest);
			}
			__result = true;
			return false;
        }
    }
}
