﻿using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using StardewValley.TerrainFeatures;
using StardewValley.Tools;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;

namespace LoveOfCooking
{
	[XmlType("Mods_blueberry_cac_custombush")] // SpaceCore serialisation signature
	public class CustomBush : Bush
	{
		public enum BushVariety
		{
			Nettle,
			Redberry
		}

		protected Texture2D CustomTexture => ModEntry.SpriteSheet;
		protected Rectangle SourceRectangle;
		protected IReflectedField<float> AlphaField, ShakeField, MaxShakeField;
		protected IReflectedMethod ShakeMethod;
		
		public BushVariety Variety;
		public int DaysToMature;
		public int DaysBetweenProduceWhenEmpty;
		public int DaysBetweenAdditionalProduce;
		public int HeldItemId = -1;
		public int HeldItemQuantity;
		public int EffectiveSize;
		public bool IsMature => this.getAge() >= DaysToMature;
		public static readonly Point NettleSize = new Point(24, 24);
		public static readonly Point RedberrySize = new Point(32, 32);

		// Nettles
		public const int NettlesDamage = 4;
		public const string NettleBuffSource = ModEntry.ObjectPrefix + "NettleBuff";

		public CustomBush()
			: base()
		{
			this.GetFields();
		}

		public CustomBush(Vector2 tile, GameLocation location, BushVariety variety)
			: base(tileLocation: tile, size: variety == BushVariety.Redberry ? 2 : 3, location: location)
		{
			currentTileLocation = tile;
			currentLocation = location;
			Variety = variety;
			this.GetFields();
			this.FirstTimeSetup();
		}

		private void GetFields()
		{
			AlphaField = ModEntry.Instance.Helper.Reflection.GetField<float>(this, "alpha"); // TODO: FIX: CustomBush.AlphaField null after SpaceCore deserialise
			ShakeField = ModEntry.Instance.Helper.Reflection.GetField<float>(this, "shakeRotation");
			MaxShakeField = ModEntry.Instance.Helper.Reflection.GetField<float>(this, "maxShake");

			ShakeMethod = ModEntry.Instance.Helper.Reflection.GetMethod(this, "shake");
		}

		private void FirstTimeSetup()
		{
			drawShadow.Set(false);
			flipped.Set(Game1.random.NextDouble() < 0.5);
			if (currentLocation.IsGreenhouse)
				greenhouseBush.Value = true;

			if (Variety == BushVariety.Nettle)
			{
				health = 20;
				EffectiveSize = 0;
				DaysToMature = -1;
				DaysBetweenProduceWhenEmpty = -1;
				DaysBetweenAdditionalProduce = -1;
				HeldItemId = ModEntry.JsonAssets.GetObjectId(ModEntry.ObjectPrefix + "nettles");
				HeldItemQuantity = 1;
			}
			else if (Variety == BushVariety.Redberry)
			{
				health = 80;
				EffectiveSize = 1;
				DaysToMature = 17;
				DaysBetweenProduceWhenEmpty = 4;
				DaysBetweenAdditionalProduce = 2;
				HeldItemId = ModEntry.JsonAssets.GetObjectId(ModEntry.ObjectPrefix + "redberry");
				HeldItemQuantity = 0;
			}
		}

		public void Shake(Vector2 tileLocation, bool doEvenIfStillShaking)
		{
			ShakeMethod.Invoke(tileLocation, doEvenIfStillShaking);
		}

		public override void dayUpdate(GameLocation environment, Vector2 tileLocation)
		{
			if (Variety == BushVariety.Redberry
			    && tileSheetOffset == 0
			    && Game1.random.NextDouble() < 0.2
			    && this.inBloom(Game1.currentSeason, Game1.dayOfMonth))
				tileSheetOffset.Value = 1;
			else if (!Game1.currentSeason.Equals("summer") && !this.inBloom(Game1.currentSeason, Game1.dayOfMonth))
				tileSheetOffset.Value = 0;

			this.SetUpSourceRectangle();
		}

		public override bool seasonUpdate(bool onLoad)
		{
			if (!Game1.IsMultiplayer || Game1.IsServer)
			{
				if (Variety == BushVariety.Redberry && Game1.currentSeason.Equals("summer") && Game1.random.NextDouble() < 0.5)
					tileSheetOffset.Value = 1;
				else
					tileSheetOffset.Value = 0;
				this.loadSprite();
			}
			return false;
		}

		public override bool isActionable()
		{
			return true;
		}
		
		public override bool performUseAction(Vector2 tileLocation, GameLocation location)
		{
			if (Math.Abs(0f - MaxShakeField.GetValue()) < 0.001f
			    && (greenhouseBush || Variety == BushVariety.Redberry || !Game1.currentSeason.Equals("winter")))
			{
				location.localSound("leafrustle");
			}

			if (Variety == BushVariety.Nettle)
			{
				DelayedAction.playSoundAfterDelay("leafrustle", 100);
				Game1.player.takeDamage(NettlesDamage + Game1.player.resilience, true, null);
				if (Game1.player.health < 1)
					Game1.player.health = 1;
				Game1.buffsDisplay.otherBuffs.RemoveAll(b => b.source == NettleBuffSource);
				Game1.buffsDisplay.addOtherBuff(new Buff(
					0, 0, 0, 0, 0, 0, 0,
					0, 0, -1, 0, 0, 10,
					NettleBuffSource, ModEntry.Instance.Helper.Translation.Get("buff.nettles.inspect")));
			}

			this.Shake(tileLocation: tileLocation, doEvenIfStillShaking: true);
			return true;
		}

		public override bool performToolAction(Tool t, int explosion, Vector2 tileLocation, GameLocation location)
		{
			if (location == null)
			{
				location = Game1.currentLocation;
			}
			if (explosion > 0)
			{
				this.Shake(tileLocation, doEvenIfStillShaking: true);
				return false;
			}
			if (t != null && ModEntry.ItemDefinitions["NettlesHarvestingTools"].Any(n => n == t.GetType().Name) && this.isDestroyable(location, tileLocation))
			{
				location.playSound("leafrustle");
				this.Shake(tileLocation, doEvenIfStillShaking: true);
				
				if (Variety == BushVariety.Nettle)
					health = -100;
				else
					health -= 50;

				if (health <= -1f)
				{
					int quantity = Game1.random.Next(HeldItemQuantity, HeldItemQuantity + 1) + (Game1.player.ForagingLevel / 4);
					StardewValley.Object heldObject = new StardewValley.Object(
						parentSheetIndex: HeldItemId,
						initialStack: 1,
						quality: Game1.player.professions.Contains(16) ? 4 : 0);
					for (int i = 0; i < quantity; ++i)
					{
						Game1.createItemDebris(item: heldObject, origin: Utility.PointToVector2(this.getBoundingBox().Center), direction: Game1.random.Next(1, 4));
					}
					if (Variety != BushVariety.Nettle)
						location.playSound("treethud");
					DelayedAction.playSoundAfterDelay("leafrustle", 100);
					Color leafColour = Color.Green;
					string season = (overrideSeason == -1) ? Game1.GetSeasonForLocation(location) : Utility.getSeasonNameFromNumber(overrideSeason);
					switch (season)
					{
						case "spring":
							leafColour = Color.Green;
							break;
						case "summer":
							leafColour = Color.ForestGreen;
							break;
						case "fall":
							leafColour = Color.IndianRed;
							break;
						case "winter":
							leafColour = Color.Cyan;
							break;
					}
					Multiplayer multiplayer = ModEntry.Instance.Helper.Reflection.GetField<Multiplayer>(typeof(Game1), "multiplayer").GetValue();
					Rectangle sourceRect = new Rectangle(355, 1200 + (season.Equals("fall") ? 16 : (season.Equals("winter") ? (-16) : 0)), 16, 16);
					int leafCount = Variety == BushVariety.Nettle ? 6 : 10;
					for (int j = 0; j <= EffectiveSize; j++)
					{
						for (int i = 0; i < leafCount; i++)
						{
							multiplayer.broadcastSprites(location, new TemporaryAnimatedSprite(
								textureName: "LooseSprites\\Cursors",
								sourceRect: sourceRect,
								position: Utility.getRandomPositionInThisRectangle(getBoundingBox(), Game1.random) - new Vector2(0f, Game1.random.Next(64)),
								flipped: false,
								alphaFade: 0.01f,
								color: leafColour)
							{
								motion = new Vector2(Game1.random.Next(-10, 11) / 10f, -Game1.random.Next(1, 4)),
								acceleration = new Vector2(0f, Game1.random.Next(13, 17) / 100f),
								accelerationChange = new Vector2(0f, -0.001f),
								scale = 2f,
								layerDepth = (tileLocation.Y + 1f) * 64f / 10000f,
								animationLength = 11,
								totalNumberOfLoops = 99,
								interval = Game1.random.Next(10, 40),
								delayBeforeAnimationStart = (j + 1) * i * 20
							});
							if (i % leafCount == 0)
							{
								multiplayer.broadcastSprites(location, new TemporaryAnimatedSprite(
									50, Utility.getRandomPositionInThisRectangle(getBoundingBox(), Game1.random) - new Vector2(32f, Game1.random.Next(32, 64)), leafColour));
								multiplayer.broadcastSprites(location, new TemporaryAnimatedSprite(
									12, Utility.getRandomPositionInThisRectangle(getBoundingBox(), Game1.random) - new Vector2(32f, Game1.random.Next(32, 64)), Color.White));
							}
						}
					}
					return true;
				}
				location.playSound("axchop");
				
			}
			return false;
		}

		public override Rectangle getBoundingBox(Vector2 tileLocation)
		{
			if (Variety == BushVariety.Nettle)
				return new Rectangle(
					(int)tileLocation.X * Game1.tileSize,
					(int)tileLocation.Y * Game1.tileSize,
					Game1.tileSize,
					Game1.tileSize);
			if (Variety == BushVariety.Redberry)
				return new Rectangle(
					(int) tileLocation.X * Game1.tileSize,
					(int) tileLocation.Y * Game1.tileSize,
					Game1.tileSize * 2,
					Game1.tileSize);
			return Rectangle.Empty;
		}

		public override void loadSprite()
		{
			Random r = new Random((int)Game1.stats.DaysPlayed
			                   + (int)Game1.uniqueIDForThisGame + (int)tilePosition.X + (int)tilePosition.Y * 777);
			if (Variety == BushVariety.Nettle && r.NextDouble() < 0.5)
			{
				tileSheetOffset.Value = 1;
			}
			else if (Variety == BushVariety.Redberry)
			{
				tileSheetOffset.Value = this.inBloom(Game1.currentSeason, Game1.dayOfMonth) ? 1 : 0;
			}
			this.SetUpSourceRectangle();
		}
		
		public void SetUpSourceRectangle()
		{
			if (Variety == BushVariety.Nettle)
			{
				SourceRectangle = new Rectangle(
					0 + (tileSheetOffset * NettleSize.X),
					16,
					NettleSize.X,
					NettleSize.Y);
			}
			else if (Variety == BushVariety.Redberry)
			{
				int seasonNumber = greenhouseBush.Value ? 0 : Utility.getSeasonNumber(Game1.currentSeason);
				int age = this.getAge();
				SourceRectangle = new Rectangle(
					(seasonNumber * RedberrySize.X) + (Math.Min(2, age / 10) * RedberrySize.X) + (tileSheetOffset * RedberrySize.X),
					16 + 16,
					RedberrySize.X,
					RedberrySize.Y);
			}
		}
		
		public override Rectangle getRenderBounds(Vector2 tileLocation)
		{
			if (Variety == BushVariety.Nettle)
				return new Rectangle((int)tileLocation.X * 64, (int)(tileLocation.Y - 1f) * 64, 64, 160);
			if (Variety == BushVariety.Redberry)
				return new Rectangle((int)tileLocation.X * 64, (int)(tileLocation.Y - 2f) * 64, 128, 256);
			return Rectangle.Empty;
		}

		public override void draw(SpriteBatch spriteBatch, Vector2 tileLocation)
		{
			Vector2 screenPosition = Game1.GlobalToLocal(Game1.viewport, new Vector2(
				tileLocation.X * 64f + 64f / 2,
				(tileLocation.Y + 1f) * 64f));
			spriteBatch.Draw(
				texture: CustomTexture,
				position: screenPosition,
				sourceRectangle: SourceRectangle,
				color: Color.White * AlphaField.GetValue(),
				rotation: ShakeField.GetValue(),
				origin: new Vector2(
					SourceRectangle.Width / 2,
					SourceRectangle.Height),
				scale: Game1.pixelZoom,
				effects: flipped ? SpriteEffects.FlipHorizontally : SpriteEffects.None,
				layerDepth: (this.getBoundingBox(tileLocation).Center.Y + 48) / 10000f - tileLocation.X / 1000000f);
		}
		
		public override void drawInMenu(SpriteBatch spriteBatch, Vector2 positionOnScreen,
			Vector2 tileLocation, float scale, float layerDepth)
		{
			layerDepth += positionOnScreen.X / 100000f;
			spriteBatch.Draw(
				texture: texture.Value,
				position: positionOnScreen + new Vector2(0f, -64f * scale),
				sourceRectangle: new Rectangle(32, 96, 16, 32),
				color: Color.White,
				rotation: 0f,
				origin: Vector2.Zero,
				scale: scale,
				effects: flipped ? SpriteEffects.FlipHorizontally : SpriteEffects.None,
				layerDepth: layerDepth + (positionOnScreen.Y + 448f * scale - 1f) / 20000f);
		}
		
		/* HarmonyPatch behaviours */

		public static bool InBloomBehaviour(CustomBush bush, string season, int dayOfMonth)
		{
			if (bush.Variety == BushVariety.Nettle)
				return false;
			bool inSeason = dayOfMonth >= 22 && (!season.Equals("winter") || bush.greenhouseBush.Value);
			return bush.IsMature && inSeason;
		}

		public static int GetEffectiveSizeBehaviour(CustomBush bush)
		{
			return bush.EffectiveSize;
		}

		public static bool IsDestroyableBehaviour(CustomBush bush)
		{
			return true;
		}

		public static void ShakeBehaviour(CustomBush bush, Vector2 tileLocation)
		{
			if (bush.Variety == BushVariety.Redberry)
			{
				for (int i = 0; i < bush.HeldItemQuantity; ++i)
					Game1.createObjectDebris(bush.HeldItemId, (int)tileLocation.X, (int)tileLocation.Y);
			}
		}

		internal static void TrySpawnNettles()
		{
			// Only master player should make changes to the world
			if (Game1.MasterPlayer.UniqueMultiplayerID != Game1.player.UniqueMultiplayerID)
				return;

			// Attempt to place a wild nettle as forage around other weeds
			bool spawnNettlesToday = Game1.currentSeason == "summer" || ((Game1.currentSeason == "spring" || Game1.currentSeason == "fall") && Game1.dayOfMonth % 3 == 1);
			if (ModEntry.NettlesEnabled && spawnNettlesToday)
			{
				foreach (string l in ModEntry.ItemDefinitions["NettlesLocations"])
				{
					if (Game1.random.NextDouble() > float.Parse(ModEntry.ItemDefinitions["NettlesDailyChancePerLocation"][0]))
					{
						// Skip the location if we didn't succeed the roll to add nettles
						Log.D($"Did not add nettles to {l}.",
							ModEntry.Instance.Config.DebugMode);
						continue;
					}

					// Spawn a random number of nettles between some upper and lower bounds, reduced by the number of nettles already in this location
					GameLocation location = Game1.getLocationFromName(l);
					int nettlesToAdd = Game1.random.Next(int.Parse(ModEntry.ItemDefinitions["NettlesAddedRange"][0]), int.Parse(ModEntry.ItemDefinitions["NettlesAddedRange"][1]));
					int nettlesAlreadyInLocation = location.Objects.Values.Count(o => o.Name.ToLower().EndsWith("nettles"));
					nettlesToAdd -= nettlesAlreadyInLocation;
					int nettlesAdded = 0;

					List<Vector2> shuffledWeedsTiles = location.Objects.Keys.Where(
						tile => location.Objects.TryGetValue(tile, out StardewValley.Object o) && o.Name == "Weeds").ToList();
					Utility.Shuffle(Game1.random, shuffledWeedsTiles);
					foreach (Vector2 tile in shuffledWeedsTiles)
					{
						if (nettlesAdded >= nettlesToAdd)
						{
							// Move to the next location if this location's quota is met
							break;
						}
						Vector2 nearbyTile = Utility.getRandomAdjacentOpenTile(tile, location);
						if (nearbyTile == Vector2.Zero)
						{
							// Skip weeds without any free spaces to spawn nettles upon
							continue;
						}
						// Spawn nettles around other weeds
						CustomBush nettleBush = new CustomBush(tile: nearbyTile, location: location, variety: BushVariety.Nettle);
						location.largeTerrainFeatures.Add(nettleBush);
						++nettlesAdded;
						Log.D($"Adding to {nearbyTile}...",
							ModEntry.Instance.Config.DebugMode);
					}

					Log.D($"Added {nettlesAdded} nettles to {l}.",
						ModEntry.Instance.Config.DebugMode);
				}
			}
		}

		internal static void ClearNettlesGlobally()
		{
			foreach (string l in ModEntry.ItemDefinitions["NettlesLocations"])
			{
				GameLocation location = Game1.getLocationFromName(l);
				Log.D($"Removing {location.largeTerrainFeatures.Count(ltf => ltf is CustomBush cb && cb.Variety == BushVariety.Nettle)} nettles from {location.Name}.",
					ModEntry.Instance.Config.DebugMode);
				for (int i = location.largeTerrainFeatures.Count - 1; i >= 0; --i)
				{
					LargeTerrainFeature ltf = location.largeTerrainFeatures[i];
					if (ltf is CustomBush customBush && customBush.Variety == BushVariety.Nettle)
					{
						Log.D($"Removing from {ltf.currentTileLocation}...",
							ModEntry.Instance.Config.DebugMode);
						location.largeTerrainFeatures.RemoveAt(i);
					}
				}
			}
		}
	}
}
