﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using KSP.Localization;

namespace SimpleBoiloff
{
    public class ModuleCryoTank: PartModule
    {

        // Cost to cool off u/s per 1000 u
        [KSPField(isPersistant = false)]
        public float CoolingCost = 0.0f;

        // Minimum EC to leave
        [KSPField(isPersistant = false)]
        public float minResToLeave = 1.0f;

        // Last timestamp that boiloff occurred
        [KSPField(isPersistant = true)]
        public double LastUpdateTime = 0;

        // Whether active tank refrigeration is occurring
        [KSPField(isPersistant = true)]
        public bool CoolingEnabled = true;

        [KSPField(isPersistant = true)]
        public bool BoiloffOccuring = false;

        // Special cooling parameters
        [KSPField(isPersistant = false)]
        public bool LongwaveFluxAffectsBoiloff = false;

        [KSPField(isPersistant = false)]
        public bool ShortwaveFluxAffectsBoiloff = false;

        // Percent of insolation reflected
        [KSPField(isPersistant = false)]
        public float Albedo = 0.5f;

        [KSPField(isPersistant = false)]
        public float ShortwaveFluxBaseline = 0.7047f;

        [KSPField(isPersistant = false)]
        public float LongwaveFluxBaseline = 0.1231f;

        [KSPField(isPersistant = false)]
        public double MinimumBoiloffScale = 0.1f;

        [KSPField(isPersistant = false)]
        public double MaximumBoiloffScale = 5f;

        [KSPField(isPersistant = false)]
        public double currentCoolingCost = 0.0;

        public bool HasResource { get { return HasResource; } }

        // PRIVATE
        private double coolingCost = 0.0;
        private List<BoiloffFuel> fuels;
        private bool hasResource = false;
        private double fuelAmount = 0.0;
        private double maxFuelAmount = 0.0;

        private double boiloffRateSeconds = 0.0;
        private double solarFlux = 1.0;
        private double planetFlux = 1.0;
        private double fluxScale = 1.0;

        // Represents a fuel that boils off
        [System.Serializable]
        public class BoiloffFuel
        {
          public string fuelName;
          public float boiloffRate;

          public PartResource resource;
          public List<ResourceRatio> outputs;

          bool fuelPresent = false;
          public float boiloffRateSeconds = 0f;
          int id = -1;
          Part part;

          public BoiloffFuel(ConfigNode node, Part p)
          {
              part = p;
              node.TryGetValue("FuelName", ref fuelName);
              node.TryGetValue("BoiloffRate", ref boiloffRate);

              outputs = new List<ResourceRatio>();
              ConfigNode[] outNodes = node.GetNodes("OUTPUT_RESOURCE");
              for (int i = 0; i < outNodes.Length; i++)
              {
                  ResourceRatio r = new ResourceRatio();
                  r.Load(outNodes[i]);
                  outputs.Add(r);
              }
          }

          public void Initialize()
          {
              if (id == -1)
                id = PartResourceLibrary.Instance.GetDefinition(fuelName).id;
              resource = part.Resources.Get(id);
              boiloffRateSeconds = boiloffRate/100f/3600f;
              fuelPresent = true;
          }
          public double FuelAmountMax()
          {
              if (fuelPresent)
                  return resource.maxAmount;
              return 0d;
          }
          public double FuelAmount()
          {
              if (fuelPresent)
                  return resource.amount;
              return 0d;
          }
          public void Boiloff(double seconds, double scale)
          {
              if (fuelPresent)
              {
                  double toBoil = resource.amount * (1.0 - Math.Pow(1.0 - boiloffRateSeconds, seconds)) * scale;
                  resource.amount = Math.Max(resource.amount - toBoil, 0d);

                  if (outputs.Count > 0)
                  {
                    for (int i = 0; i < outputs.Count; i++)
                    {
                      part.RequestResource(outputs[i].ResourceName, -toBoil*outputs[i].Ratio, outputs[i].FlowMode);
                    }
                  }
              }
          }


        }

        // UI FIELDS/ BUTTONS
        // Status string
        [KSPField(isPersistant = false, guiActive = true, guiName = "Boiloff")]
        public string BoiloffStatus = "N/A";

        [KSPField(isPersistant = false, guiActive = false, guiName = "Insulation")]
        public string CoolingStatus = "N/A";

        [KSPEvent(guiActive = false, guiName = "Enable Cooling", active = true)]
        public void Enable()
        {
            CoolingEnabled = true;
        }
        [KSPEvent(guiActive = false, guiName = "Disable Cooling", active = false)]
        public void Disable()
        {
            CoolingEnabled = false;
        }

        // DEBUG FIELDS

        [KSPField(isPersistant = true)]
        public bool DebugMode = false;

        [KSPField(isPersistant = false, guiActive = false, guiName = "Albedo")]
        public string D_Albedo = "N/A";

        [KSPField(isPersistant = false, guiActive = false, guiName = "Emissivity")]
        public string D_Emiss = "N/A";

        [KSPField(isPersistant = false, guiActive = false, guiName = "SW_In(kW)")]
        public string D_InSolar = "N/A";

        [KSPField(isPersistant = false, guiActive = false, guiName = "LW_In(kW)")]
        public string D_InPlanet = "N/A";

        [KSPField(isPersistant = false, guiActive = false, guiName = "NetRad_In(kW)")]
        public string D_NetRad = "N/A";

        // ACTIONS
        [KSPAction("Enable Cooling")]
        public void EnableAction(KSPActionParam param) { Enable(); }

        [KSPAction("Disable Cooling")]
        public void DisableAction(KSPActionParam param) { Disable(); }

        [KSPAction("Toggle Cooling")]
        public void ToggleAction(KSPActionParam param)
        {
            CoolingEnabled = !CoolingEnabled;
        }

        // REWRITE ME
        public override string GetInfo()
        {

          string msg;
          string fuelDisplayName;
            if (CoolingCost > 0.0f)
            {
              string sub = "";
              foreach(BoiloffFuel fuel in fuels)
              {
                fuelDisplayName = PartResourceLibrary.Instance.GetDefinition(fuel.fuelName).displayName;
                sub += Localizer.Format("#LOC_CryoTanks_ModuleCryoTank_PartInfoBoiloff", fuelDisplayName, (fuel.boiloffRate).ToString("F2"));
                if (fuel.outputs.Count > 0)
                {
                  foreach (ResourceRatio output in fuel.outputs)
                  {
                    string outputDisplayName = PartResourceLibrary.Instance.GetDefinition(output.ResourceName).displayName;
                    sub +=Localizer.Format("#LOC_CryoTanks_ModuleCryoTank_PartInfoBoiloffOutput", outputDisplayName, (fuel.boiloffRate*output.Ratio).ToString("F2"));
                  }
                }
              }

              msg = Localizer.Format("#LOC_CryoTanks_ModuleCryoTank_PartInfoCooled",sub,  CoolingCost.ToString("F2"));

            } else
            {
              msg = Localizer.Format("#LOC_CryoTanks_ModuleCryoTank_PartInfoUncooled");
              foreach(BoiloffFuel fuel in fuels)
              {
                fuelDisplayName = PartResourceLibrary.Instance.GetDefinition(fuel.fuelName).displayName;
                msg += Localizer.Format("#LOC_CryoTanks_ModuleCryoTank_PartInfoBoiloff",  fuelDisplayName, (fuel.boiloffRate).ToString("F2"));
                if (fuel.outputs.Count > 0)
                {
                    foreach (ResourceRatio output in fuel.outputs)
                    {
                        string outputDisplayName = PartResourceLibrary.Instance.GetDefinition(output.ResourceName).displayName;
                        msg += Localizer.Format("#LOC_CryoTanks_ModuleCryoTank_PartInfoBoiloffOutput", outputDisplayName, (fuel.boiloffRate * output.Ratio).ToString("F2"));
                    }
                }
              }
            }
          return msg;
        }


        public override void OnStart(StartState state)
        {
            Fields["BoiloffStatus"].guiName = Localizer.Format("#LOC_CryoTanks_ModuleCryoTank_Field_BoiloffStatus");
            Fields["CoolingStatus"].guiName = Localizer.Format("#LOC_CryoTanks_ModuleCryoTank_Field_CoolingStatus");

            Events["Enable"].guiName = Localizer.Format("#LOC_CryoTanks_ModuleCryoTank_Event_Enable");
            Events["Disable"].guiName = Localizer.Format("#LOC_CryoTanks_ModuleCryoTank_Event_Disable");

            Actions["EnableAction"].guiName =  Localizer.Format("#LOC_CryoTanks_ModuleCryoTank_Action_EnableAction");
            Actions["DisableAction"].guiName =  Localizer.Format("#LOC_CryoTanks_ModuleCryoTank_Action_DisableAction");
            Actions["ToggleAction"].guiName =  Localizer.Format("#LOC_CryoTanks_ModuleCryoTank_Action_ToggleAction");

            if (HighLogic.LoadedSceneIsFlight || HighLogic.LoadedSceneIsEditor)
            {
              if (fuels == null || fuels.Count == 0)
              {
                  Debug.Log(part.partInfo.name);
                  ConfigNode cfg;
                  foreach (UrlDir.UrlConfig pNode in GameDatabase.Instance.GetConfigs("PART"))
                  {
                      if (pNode.name.Replace("_", ".") == part.partInfo.name)
                      {
                          cfg = pNode.config;
                          ConfigNode node = cfg.GetNodes("MODULE").Single(n => n.GetValue("name") == moduleName);
                          OnLoad(node);
                      }
                  }
              }
              if (DebugMode)
              {
                  Fields["D_NetRad"].guiActive = true;
                  Fields["D_InSolar"].guiActive = true;
                  Fields["D_InPlanet"].guiActive = true;
                  Fields["D_Albedo"].guiActive = true;
                  Fields["D_Emiss"].guiActive = true;

              }
            }

            if (HighLogic.LoadedSceneIsFlight)
            {
                hasResource = false;
                foreach(BoiloffFuel fuel in fuels)
                {
                  if (isResourcePresent(fuel.fuelName))
                  {
                    hasResource = true;
                    fuel.Initialize();
                  }
                }
                if (!hasResource)
                {
                    Events["Disable"].guiActive = false;
                    Events["Enable"].guiActive = false;
                    Fields["BoiloffStatus"].guiActive = false;
                    return;
                }
                maxFuelAmount = GetTotalMaxResouceAmount();
                planetFlux = LongwaveFluxBaseline;
                solarFlux = ShortwaveFluxBaseline;

              if (CoolingCost > 0.0)
              {
                coolingCost = maxFuelAmount/1000.0 * CoolingCost;
                Events["Disable"].guiActive = true;
                Events["Enable"].guiActive = true;
                Events["Enable"].guiActiveEditor = true;
                Events["Disable"].guiActiveEditor = true;
              }
              // Catchup
              DoCatchup();
            }
        }
        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);

            ConfigNode[] varNodes = node.GetNodes("BOILOFFCONFIG");
            fuels = new List<BoiloffFuel>();
            for (int i=0; i < varNodes.Length; i++)
            {
              fuels.Add(new BoiloffFuel(varNodes[i], this.part));
            }
        }

        public void DoCatchup()
        {
          if (part.vessel.missionTime > 0.0)
          {
              double currentEC = 0d;
              double maxAmount = 0d;
              vessel.GetConnectedResourceTotals(PartResourceLibrary.Instance.GetDefinition("ElectricCharge").id, out currentEC, out maxAmount);
              // no consumption here anymore, since we know, that there won't be enough EC
              if((currentEC - minResToLeave) < (coolingCost * TimeWarp.fixedDeltaTime))
              {
                  double elapsedTime = part.vessel.missionTime - LastUpdateTime;
                  for (int i = 0; i < fuels.Count ; i++)
                    fuels[i].Boiloff(elapsedTime, 1.0);
              }
          }
        }

        public void Update()
        {
          if (HighLogic.LoadedSceneIsFlight && hasResource)
          {
            // Show the insulation status field if there is a cooling cost
            if (CoolingCost > 0f)
            {
              Fields["CoolingStatus"].guiActive = true;
              if (Events["Enable"].active == CoolingEnabled || Events["Disable"].active != CoolingEnabled)
                {
                    Events["Disable"].active = CoolingEnabled;
                    Events["Enable"].active = !CoolingEnabled;
               }
            }
            if (fuelAmount == 0.0)
            {

                Fields["BoiloffStatus"].guiActive = false;
            }
          }
          else if (HighLogic.LoadedSceneIsEditor)
          {
              hasResource = false;
              foreach(BoiloffFuel fuel in fuels)
              {
                if (isResourcePresent(fuel.fuelName))
                {
                  hasResource = true;
                  fuel.Initialize();
                }
              }
              if (CoolingCost > 0f && hasResource)
              {
                  Fields["CoolingStatus"].guiActive = true;

                  double max = GetTotalMaxResouceAmount();

                  CoolingStatus =  Localizer.Format("#LOC_CryoTanks_ModuleCryoTank_Field_CoolingStatus_Editor", (CoolingCost * (float)(max / 1000.0)).ToString("F2"));

                  Events["Disable"].guiActiveEditor = true;
                  Events["Enable"].guiActiveEditor = true;

                  if (Events["Enable"].active == CoolingEnabled || Events["Disable"].active != CoolingEnabled)
                  {
                      Events["Disable"].active = CoolingEnabled;
                      Events["Enable"].active = !CoolingEnabled;
                  }
              }
              else
              {
                  Fields["CoolingStatus"].guiActive = false;
                  Events["Disable"].guiActiveEditor = false;
                  Events["Enable"].guiActiveEditor = false;
              }
          }

        }
        protected void FixedUpdate()
        {
            if (HighLogic.LoadedSceneIsFlight && hasResource)
            {
                if (part.ptd != null)
                {
                    if (LongwaveFluxAffectsBoiloff)
                        planetFlux = part.ptd.bodyFlux;

                    if (ShortwaveFluxAffectsBoiloff)
                        solarFlux = part.ptd.sunFlux / part.emissiveConstant;
                }
                if (ShortwaveFluxAffectsBoiloff || LongwaveFluxAffectsBoiloff)
                {
                    fluxScale = Math.Max((planetFlux / LongwaveFluxBaseline + (solarFlux * Albedo) / ShortwaveFluxBaseline) / 2.0f, MinimumBoiloffScale);
                }
                else
                {
                    fluxScale = 1.0;
                }

                fuelAmount = GetTotalResouceAmount();

                // If we have no fuel, no need to do any calculations
                if (fuelAmount == 0.0)
                {
                    BoiloffStatus = Localizer.Format("#LOC_CryoTanks_ModuleCryoTank_Field_BoiloffStatus_NoFuel");
                    CoolingStatus = Localizer.Format("#LOC_CryoTanks_ModuleCryoTank_Field_CoolingStatus_NoFuel");
                    currentCoolingCost = 0.0;
                    return;
                }

                // If the cooling cost is zero, we must boil off
                if (coolingCost == 0f)
                {
                    BoiloffOccuring = true;
                    BoiloffStatus = FormatRate(GetTotalBoiloffRate() * fuelAmount * fluxScale);
                    currentCoolingCost = 0.0;
                }
                // else check for available power
                else
                {
                    if (!CoolingEnabled)
                    {
                        BoiloffOccuring = true;
                        BoiloffStatus = FormatRate(GetTotalBoiloffRate() * fuelAmount * fluxScale);
                        CoolingStatus = Localizer.Format("#LOC_CryoTanks_ModuleCryoTank_Field_CoolingStatus_Disabled");
                        currentCoolingCost = 0.0;
                    }
                    else
                    {
                        ConsumeCharge();
                        currentCoolingCost = coolingCost;
                    }

                  }

                if (BoiloffOccuring)
                {
                    DoBoiloff();
                }
                if (part.vessel.missionTime > 0.0)
                {
                    LastUpdateTime = part.vessel.missionTime;
                }

            }
            if (HighLogic.LoadedSceneIsFlight && DebugMode)
            {
                D_Albedo = String.Format("{0:F4}", Albedo);
                D_Emiss = String.Format("{0:F4}", part.emissiveConstant);
                D_InPlanet = String.Format("{0:F4}", planetFlux);
                D_InSolar = String.Format("{0:F4}", solarFlux*Albedo);
                D_NetRad = String.Format("{0:F4}", fluxScale);
            }
        }
        // Returns the cooling cost if the system is enabled
        public double GetCoolingCost()
        {
          if (CoolingEnabled)
          {
            return coolingCost;
          }
          return 0d;
        }

        public double SetBoiloffState(bool state)
        {
          if (CoolingEnabled && coolingCost > 0f)
          {
            if (state)
            {
              BoiloffOccuring = true;
              BoiloffStatus = FormatRate(boiloffRateSeconds * fuelAmount);
              CoolingStatus = Localizer.Format("#LOC_CryoTanks_ModuleCryoTank_Field_CoolingStatus_Uncooled");
            } else
            {

              BoiloffOccuring = false;
              BoiloffStatus = String.Format("Insulated");
              CoolingStatus = Localizer.Format("#LOC_CryoTanks_ModuleCryoTank_Field_CoolingStatus_Cooling", coolingCost.ToString("F2"));

            }
            return (double)coolingCost;
        }
        return 0d;


        }

        public void ConsumeCharge()
        {
          if (CoolingEnabled && coolingCost > 0f)
          {
            double chargeRequest = coolingCost * TimeWarp.fixedDeltaTime;

            double currentEC = 0d;
            double maxEC = 0d;
            vessel.GetConnectedResourceTotals(PartResourceLibrary.Instance.GetDefinition("ElectricCharge").id, out currentEC, out maxEC);

            // only use EC if there is more then minResToLeave left
            double req = 0;
            if (currentEC > chargeRequest + minResToLeave)
            {
                req = part.RequestResource("ElectricCharge", chargeRequest);
            }

            //Debug.Log(req.ToString() + " rec, wanted "+ chargeRequest.ToString());
            // Fully cooled
            double tolerance = 0.0001;
            if (req >= chargeRequest - tolerance)
            {
                BoiloffOccuring = false;
                BoiloffStatus = Localizer.Format("#LOC_CryoTanks_ModuleCryoTank_Field_BoiloffStatus_Insulated");
                CoolingStatus = Localizer.Format("#LOC_CryoTanks_ModuleCryoTank_Field_CoolingStatus_Cooling", coolingCost.ToString("F2"));
            }
            else
            {
                BoiloffOccuring = true;
                BoiloffStatus = FormatRate(boiloffRateSeconds * fuelAmount);
                CoolingStatus = Localizer.Format("#LOC_CryoTanks_ModuleCryoTank_Field_CoolingStatus_Uncooled");
            }
          }
        }


        protected void DoBoiloff()
        {
            for (int i = 0; i < fuels.Count ; i++)
                fuels[i].Boiloff(TimeWarp.fixedDeltaTime, fluxScale);
        }

        protected string FormatRate(double rate)
        {
            double adjRate = rate;
            string interval = Localizer.Format("#LOC_CryoTanks_ModuleCryoTank_TimeInterval_Second_Abbrev");
            if (adjRate < 0.01)
            {
                adjRate = adjRate*60.0;
                interval = Localizer.Format("#LOC_CryoTanks_ModuleCryoTank_TimeInterval_Minute_Abbrev");
            }
            if (adjRate < 0.01)
            {
                adjRate = adjRate * 60.0;
                interval = Localizer.Format("#LOC_CryoTanks_ModuleCryoTank_TimeInterval_Hour_Abbrev");
            }
            return Localizer.Format("#LOC_CryoTanks_ModuleCryoTank_Field_BoiloffStatus_Boiloff", adjRate.ToString("F2"), interval.ToString());
        }
        public bool isResourcePresent(string nm)
        {
            int id = PartResourceLibrary.Instance.GetDefinition(nm).id;
            PartResource res = this.part.Resources.Get(id);
            if (res == null)
                return false;
            return true;
        }
        protected double GetTotalResouceAmount()
        {
            double max = 0d;
            for (int i = 0; i < fuels.Count; i++)
                max += fuels[i].FuelAmount();
            return max;
        }
        protected double GetTotalMaxResouceAmount()
        {
            double max = 0d;
            for (int i = 0; i < fuels.Count; i++)
                max += fuels[i].FuelAmountMax();
            return max;
        }
        protected double GetTotalBoiloffRate()
        {
            double max = 0d;
            for (int i = 0; i < fuels.Count ; i++)
              max += fuels[i].boiloffRateSeconds;
            return max;
        }

        protected double GetResourceAmount(string nm)
        {
            PartResource res = this.part.Resources.Get(PartResourceLibrary.Instance.GetDefinition(nm).id);
            return res.amount;
        }
        protected double GetMaxResourceAmount(string nm)
        {
            int id = PartResourceLibrary.Instance.GetDefinition(nm).id;
            PartResource res = this.part.Resources.Get(id);
            return res.maxAmount;
        }

    }
}
