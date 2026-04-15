using UnityEngine;
using Verse;
using RimWorld;
using System.Collections.Generic;

namespace AutoHarvestWild
{

    [StaticConstructorOnStartup]
    public static class AutoHarvestDatabase
    {
        public static List<ThingDef> GenericTrees = new List<ThingDef>();
        public static List<ThingDef> WildPlants = new List<ThingDef>();

        private static readonly HashSet<string> Blacklist = new HashSet<string>
        {
            "Plant_Fibercorn",
            "Plant_BonsaiTree"
        };

        // This runs once when the game loads the main menu
        static AutoHarvestDatabase()
        {
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefs)
            {
                //Check if its a plant
                if (def.category == ThingCategory.Plant && def.plant != null)
                {
                    if (def.plant.isStump) continue;

                    //Check if its a tree
                    if (def.plant.IsTree)
                    {
                        if (Blacklist.Contains(def.defName)) continue;//ignore trees on blacklist

                        //Special trees (such as Gauranlen and Anima) have addition special properties or
                        //"comps". Ignore these trees.
                        if (def.comps == null || def.comps.Count == 0)
                        {
                            GenericTrees.Add(def);
                        }
                    }
                    //Find all other plants that are havestable, but can't be planted
                    else if (def.plant.harvestedThingDef != null && !def.plant.Sowable)
                    {
                        WildPlants.Add(def);
                    } 
                }
            }

            //Sort the WildPlants list alphabetically so the UI menu looks nice
            WildPlants.Sort((a, b) => a.label.CompareTo(b.label));
        }

    }

    public class Building_AutoHarvestCenter : Building
    {
        // Local settings for THIS specific building
        public Dictionary<string, bool> localToggles = new Dictionary<string, bool>();
        public Dictionary<string, float> localRadii = new Dictionary<string, float>();

        public bool harvestGenericTrees = false;
        public float treeRadius = 25f;

        public float radiusToDrawThisFrame = -1f;

        private int tickCounter = 0;
        private const int TicksBetweenScans = 10; // Runs every 10 "Rare" ticks (2500 normal ticks)

        //Temporary flag to track if the UI was touched
        public bool settingsChanged = false;

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
        }

        // Save and Load the data for this specific building to the save file
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref harvestGenericTrees, "harvestGenericTrees", false);
            Scribe_Values.Look(ref treeRadius, "treeRadius", 25f);
            Scribe_Collections.Look(ref localToggles, "localToggles", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref localRadii, "localRadii", LookMode.Value, LookMode.Value);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (localToggles == null)
                {
                    localToggles = new Dictionary<string, bool>();
                }

                if (localRadii == null)
                {
                    localRadii = new Dictionary<string, float>();
                }
            }
        }

        public override void TickRare() //runs every 250 ticks
        {
            base.TickRare();
            tickCounter++;

            if (tickCounter >= TicksBetweenScans)
            {
                //var stopWatch = Stopwatch.StartNew();

                tickCounter = 0;
                ScanAndMark(); // runs every 2500 ticks (approx 40 secs at 1x speed)

                //stopWatch.Stop();
                //Log.Message(stopWatch.Elapsed);
            }
        }

        public void ScanAndMark()
        {
            IntVec3 center = this.Position;

            // 1. Scan  Wild Plants
            foreach (var entry in localToggles)
            {
                if (!entry.Value) continue; // Skip if toggled off

                ThingDef plantDef = ThingDef.Named(entry.Key);
                if (plantDef == null) continue;

                float radius = localRadii.ContainsKey(entry.Key) ? localRadii[entry.Key] : 25f;
                List<Thing> plants = Map.listerThings.ThingsOfDef(plantDef);
                DesignationManager desMan = Map.designationManager;

                for (int i = 0; i < plants.Count; i++)
                {
                    if (plants[i] is Plant plant && plant.Growth >= 1f && (radius > 99f || plant.Position.InHorDistOf(center, radius)))
                    {
                        if (desMan.DesignationOn(plant, DesignationDefOf.HarvestPlant) == null)
                        {
                            desMan.AddDesignation(new Designation(plant, DesignationDefOf.HarvestPlant));
                        }
                    }
                }
            }

            // 2. Scan for all tree types
            if (harvestGenericTrees)
            {
                for (int j = 0; j < AutoHarvestDatabase.GenericTrees.Count; j++)
                {
                    ThingDef treeDef = AutoHarvestDatabase.GenericTrees[j];
                    List<Thing> treesOnMap = Map.listerThings.ThingsOfDef(treeDef);
                    DesignationManager desMan = Map.designationManager;

                    for (int i = 0; i < treesOnMap.Count; i++)
                    {
                        if (treesOnMap[i] is Plant tree && tree.Growth >= 1f && (treeRadius > 99f || tree.Position.InHorDistOf(center, treeRadius)))
                        {
                            if (desMan.DesignationOn(tree, DesignationDefOf.HarvestPlant) == null)
                            {
                                desMan.AddDesignation(new Designation(tree, DesignationDefOf.HarvestPlant));
                            }
                        }
                    }
                }
            }
        }

        //draw the visible radius when hovering over slider in tab
        public override void DrawExtraSelectionOverlays()
        {
            base.DrawExtraSelectionOverlays();

            // 80 is the safe cutoff for the precalculated list
            if (radiusToDrawThisFrame > 0f && radiusToDrawThisFrame < 79f)
            {
                // Standard high-precision ring
                GenDraw.DrawRadiusRing(this.Position, radiusToDrawThisFrame);
            }

            radiusToDrawThisFrame = -1f;
        }

    }


    public class ITab_AutoHarvestCenter : ITab
    {
        private Vector2 scrollPosition;
        private float viewHeight = 500f;

        // The building currently selected by the player
        protected Building_AutoHarvestCenter SelectedCenter => (Building_AutoHarvestCenter)SelObject;


        public ITab_AutoHarvestCenter()
        {
            this.size = new Vector2(460f, 450f);
            this.labelKey = "Harvest"; // Text on the tab button
        }

        // runs when the tab is drawn on screen
        protected override void FillTab()
        {
            Building_AutoHarvestCenter center = SelectedCenter;
            if (center == null) return;

            Rect mainRect = new Rect(0f, 0f, this.size.x, this.size.y).ContractedBy(10f);

            Text.Font = GameFont.Medium;
            Rect titleRect = new Rect(mainRect.x, mainRect.y, mainRect.width, 30f);
            Widgets.Label(titleRect, "Auto-Harvest Configuration");
            Widgets.DrawLineHorizontal(mainRect.x, mainRect.y + 30f, mainRect.width);

            Text.Font = GameFont.Small;
            Rect outRect = new Rect(mainRect.x, mainRect.y + 35f, mainRect.width, mainRect.height - 35f);
            Rect viewRect = new Rect(0f, 0f, outRect.width - 20f, viewHeight);

            Widgets.BeginScrollView(outRect, ref scrollPosition, viewRect);

            Listing_Standard listing = new Listing_Standard();
            Rect listingRect = new Rect(0f, 0f, viewRect.width, 99999f);
            listing.Begin(listingRect);
            listing.Gap(); 

            // loop dynamically builds the menu for every valid plant in the game
            foreach (ThingDef plantDef in AutoHarvestDatabase.WildPlants)
            {
                // LabelCap automatically capitalizes the first letter of the plant's name
                DrawPlantRow(listing, center, plantDef.LabelCap, plantDef.defName);
            }

            // --- GENERIC TREES ---
            Rect treeCheckRect = listing.GetRect(24f);
            bool oldTreeCheck = center.harvestGenericTrees; // Track old value
            Widgets.CheckboxLabeled(treeCheckRect, "Lumber Harvesting", ref center.harvestGenericTrees);

            if (Mouse.IsOver(treeCheckRect) && center.harvestGenericTrees)
            {
                center.radiusToDrawThisFrame = center.treeRadius;
            }

            Rect treeSliderRect = listing.GetRect(24f);
            string treeLabel = center.treeRadius > 99 ? "Radius: Map-Wide" : $"Radius: {Mathf.RoundToInt(center.treeRadius)}";

            float oldTreeRadius = center.treeRadius; // Track old value
            center.treeRadius = Widgets.HorizontalSlider(treeSliderRect, center.treeRadius, 5f, 105f, false, treeLabel);

            if (oldTreeRadius != center.treeRadius) center.settingsChanged = true; //flag changes

            if (Mouse.IsOver(treeSliderRect))
            {
                center.radiusToDrawThisFrame = center.treeRadius;
            }


            if (Event.current.type == EventType.Layout)
            {
                viewHeight = listing.CurHeight + 20f;
            }

            listing.End();
            Widgets.EndScrollView();

            // If a setting was changed AND the player is no longer holding the left mouse button (0)
            if (center.settingsChanged && !Input.GetMouseButton(0))
            {
                center.settingsChanged = false; 
                center.ScanAndMark();
            }
        }

        private void DrawPlantRow(Listing_Standard listing, Building_AutoHarvestCenter center, string label, string defName)
        {
            bool check = center.localToggles.ContainsKey(defName) && center.localToggles[defName];
            float radius = center.localRadii.ContainsKey(defName) ? center.localRadii[defName] : 25f;

             float oldRadius = radius;

            // 1. Get a specific space for the checkbox and draw it
            Rect checkRect = listing.GetRect(24f);
            Widgets.CheckboxLabeled(checkRect, label, ref check);

            // If the mouse is hovering over the checkbox AND it's turned on, queue the visible radisu
            if (Mouse.IsOver(checkRect) && check)
            {
                center.radiusToDrawThisFrame = radius;
            }

            Rect sliderRect = listing.GetRect(24f);
            string radiusLabel = radius > 99 ? "Radius: Map-Wide" : $"Radius: {Mathf.RoundToInt(radius)}";

            // Draw the slider manually
            radius = Widgets.HorizontalSlider(sliderRect, radius, 5f, 105f, false, radiusLabel);

            // If the mouse is hovering over the slider, queue the drawing
            if (Mouse.IsOver(sliderRect))
            {
                center.radiusToDrawThisFrame = radius;
            }

            // Flag changes
            if (oldRadius != radius)
            {
                center.settingsChanged = true;
            }

            center.localToggles[defName] = check;
            center.localRadii[defName] = radius;

            listing.Gap(24f);
        }
    }

}