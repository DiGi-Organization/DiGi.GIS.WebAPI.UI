using DiGi.Analytical.Building.Classes;
using DiGi.Core.Classes;
using DiGi.Geometry.Spatial.Classes;
using DiGi.Geometry.Spatial.Interfaces;
using DiGi.GIS.WebAPI.UI.Classes;
using DiGi.Solar.Classes;
using DiGi.Solar.Interfaces;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;

namespace DiGi.GIS.WebAPI.UI
{
    public static partial class Create
    {
        /// <summary>
        /// Solves the solar radiation of every receiving surface of a shading model over a weather series, in the stored form: the summary values over the weather and the quantized shading factor of every hour of the solar reference year.
        /// <para>The shading solver samples all <see cref="Constants.Default.SolarReferenceHourCount"/> reference hours (<see cref="Query.SolarReferenceDateTimes"/>), not the hours the weather happens to hold: a factor depends on the sun position only, so the stored factors are valid for any weather source (DiGi.GIS.WebAPI.UI#73). For a complete EPW year the sampled sun-up hours are the same as before. The summary values are aggregated from the exact factors (<see cref="SurfaceSolarRadiation(SolarHourSeries?, string?, double, Vector3D?, double, double, IList{double}?)"/>), so they equal what <see cref="SurfaceSolarRadiationResults(ShadingModel?, IDictionary{string, Vector3D}?, EPW.Classes.EPWFile?, ShadingSolverOptions?, Action{string}?)"/> reports; only the stored bytes are quantized.</para>
        /// <para>A surface with a sun-up hour the solver left without a factor carries no stored factors (and is logged), so its building is shown but not stored.</para>
        /// </summary>
        /// <param name="shadingModel">The shading model, stamped with its coordinates and UTC offset.</param>
        /// <param name="normals">The outward unit normal of each receiving surface, by the <c>GuidReference</c> string of its component.</param>
        /// <param name="weather">The weather series; an <see cref="EPW.Classes.EPWFile"/> is passed as is.</param>
        /// <param name="shadingSolverOptions">The solver options; their time series is replaced by the reference hours.</param>
        /// <param name="log">Receives one line of timings, or null.</param>
        /// <returns>One surface per receiving surface the model holds, or null when an input is missing, the model is unstamped, the weather holds no usable hour or the solve fails.</returns>
        public static List<GIS.Classes.SurfaceSolarRadiation>? SurfaceSolarRadiations(this ShadingModel? shadingModel, IDictionary<string, Vector3D>? normals, Weather.Classes.Weather? weather, ShadingSolverOptions? shadingSolverOptions, Action<string>? log = null)
        {
            if (shadingModel is null || normals is null)
            {
                return null;
            }

            SolarHourSeries? solarHourSeries = weather.SolarHourSeries(shadingModel.Coordinates, shadingModel.UTC);
            if (solarHourSeries is null)
            {
                return null;
            }

            List<DateTime> dateTimes_Reference = Query.SolarReferenceDateTimes();

            ShadingSolverOptions shadingSolverOptions_Temp = shadingSolverOptions is null ? new() : new(shadingSolverOptions);
            shadingSolverOptions_Temp.TimeSeries = new DateTimeCollection(dateTimes_Reference);

            Stopwatch stopwatch = Stopwatch.StartNew();

            ShadingSolver shadingSolver = new(shadingModel, shadingSolverOptions_Temp);
            if (!shadingSolver.Solve())
            {
                return null;
            }

            long milliseconds_Solve = stopwatch.ElapsedMilliseconds;
            stopwatch.Restart();

            // Unblocked share of each receiver's sky and ground; a receiver without one keeps the open view.
            Dictionary<string, ViewFactorResult> viewFactorResults = [];
            List<ViewFactorResult>? viewFactorResults_List = Solar.Create.ViewFactorResults(shadingModel, normals, shadingSolverOptions_Temp.Tolerance);
            if (viewFactorResults_List is not null)
            {
                foreach (ViewFactorResult viewFactorResult in viewFactorResults_List)
                {
                    if (viewFactorResult.Reference is string reference_ViewFactor)
                    {
                        viewFactorResults[reference_ViewFactor] = viewFactorResult;
                    }
                }
            }

            long milliseconds_ViewFactor = stopwatch.ElapsedMilliseconds;
            stopwatch.Restart();

            // Whether the sun is up in each reference hour: the hours the solver must have answered.
            bool[] sunUps_Reference = new bool[dateTimes_Reference.Count];
            for (int i = 0; i < dateTimes_Reference.Count; i++)
            {
                sunUps_Reference[i] = Solar.Query.SunDirection(shadingModel, dateTimes_Reference[i], false) is not null;
            }

            List<GIS.Classes.SurfaceSolarRadiation> result = [];
            int count_Missing = 0;

            List<IShadingElement>? shadingElements = shadingModel.GetShadingElements<IShadingElement>(false);
            if (shadingElements is not null)
            {
                foreach (IShadingElement shadingElement in shadingElements)
                {
                    string? reference = shadingElement?.Reference;
                    if (reference is null || !normals.TryGetValue(reference, out Vector3D? normal) || normal is null)
                    {
                        continue;
                    }

                    IPolygonalFace3D? polygonalFace3D = shadingElement!.PolygonalFace3D;
                    double area = polygonalFace3D is null ? double.NaN : polygonalFace3D.GetArea();
                    if (!double.IsFinite(area) || area <= 0)
                    {
                        continue;
                    }

                    if (!shadingModel.TryGetShadingFactors(shadingElement, out Dictionary<DateTime, double>? shadingFactors_Solved) || shadingFactors_Solved is null)
                    {
                        continue;
                    }

                    // Night hours are 0; a sun-up hour the solver did not answer is NaN, which the aggregation skips.
                    double[] shadingFactors = new double[dateTimes_Reference.Count];
                    for (int i = 0; i < dateTimes_Reference.Count; i++)
                    {
                        if (!sunUps_Reference[i])
                        {
                            continue;
                        }

                        if (shadingFactors_Solved.TryGetValue(dateTimes_Reference[i], out double shadingFactor_Solved))
                        {
                            shadingFactors[i] = Math.Clamp(shadingFactor_Solved, 0, 1);
                        }
                        else
                        {
                            shadingFactors[i] = double.NaN;
                            count_Missing++;
                        }
                    }

                    double skyVisibility = 1;
                    double groundVisibility = 1;
                    if (viewFactorResults.TryGetValue(reference, out ViewFactorResult? viewFactorResult_Receiver))
                    {
                        skyVisibility = viewFactorResult_Receiver.SkyVisibility;
                        groundVisibility = viewFactorResult_Receiver.GroundVisibility;
                    }

                    GIS.Classes.SurfaceSolarRadiation? surfaceSolarRadiation = solarHourSeries.SurfaceSolarRadiation(reference, area, normal, skyVisibility, groundVisibility, shadingFactors);
                    if (surfaceSolarRadiation is not null)
                    {
                        result.Add(surfaceSolarRadiation);
                    }
                }
            }

            if (log is not null)
            {
                Dictionary<DateTime, Vector3D> sunDirections_Day = [];
                for (int i = 0; i < solarHourSeries.Count; i++)
                {
                    if (solarHourSeries.SunUps[i] && Solar.Query.SunDirection(shadingModel, solarHourSeries.DateTimes[i], false) is Vector3D sunDirection_Day)
                    {
                        sunDirections_Day[solarHourSeries.DateTimes[i]] = sunDirection_Day;
                    }
                }

                int count_Group = Solar.Query.GroupDirections(sunDirections_Day, shadingSolverOptions_Temp.AngleTolerance)?.Count ?? 0;
                log(string.Format(CultureInfo.InvariantCulture, "Solar radiation: {0} hours, {1} direction groups, solve {2} ms, view factors {3} ms, aggregation {4} ms.", solarHourSeries.Count, count_Group, milliseconds_Solve, milliseconds_ViewFactor, stopwatch.ElapsedMilliseconds));
                if (count_Missing > 0)
                {
                    log(string.Format(CultureInfo.InvariantCulture, "Solar radiation: {0} sun-up receiver-hours have no shading factor; their hours are skipped and the result is not stored.", count_Missing));
                }
            }

            return result;
        }

        /// <summary>
        /// Rebuilds the summary values of a stored solar radiation result for another weather series, from its stored shading factors, normals and visibilities: no neighbours, no shading model, no solve.
        /// <para>The factors depend on the sun position only, so they hold for any weather. The weather is read hour by hour through the same aggregation as a solve (<see cref="SurfaceSolarRadiation(SolarHourSeries?, string?, double, Vector3D?, double, double, IList{double}?)"/>) and the sun is positioned from the building's location and UTC offset - the ones its shading model was solved with - so the rebuild differs from a solve only by the quantization of the factors (at most 0.5/255 per hour).</para>
        /// </summary>
        /// <param name="buildingSolarRadiationResult">The stored result, read with its shading factors.</param>
        /// <param name="buildingInformation">The building's information after <c>GIS.Analytical.Modify.UpdateBuildingInformation</c>: its coordinates and UTC offset.</param>
        /// <param name="weather">The weather series to rebuild for; an <see cref="EPW.Classes.EPWFile"/> is passed as is.</param>
        /// <returns>One surface per stored surface, carrying the stored factors, or null when an input is missing, the weather holds no usable hour, or a stored surface has no normal or no factors.</returns>
        public static List<GIS.Classes.SurfaceSolarRadiation>? SurfaceSolarRadiations(this GIS.Classes.BuildingSolarRadiationResult? buildingSolarRadiationResult, BuildingInformation? buildingInformation, Weather.Classes.Weather? weather)
        {
            List<GIS.Classes.SurfaceSolarRadiation>? surfaceSolarRadiations = buildingSolarRadiationResult?.SurfaceSolarRadiations;
            if (surfaceSolarRadiations is null || surfaceSolarRadiations.Count == 0 || buildingInformation is null)
            {
                return null;
            }

            SolarHourSeries? solarHourSeries = weather.SolarHourSeries(buildingInformation.Coordinates, buildingInformation.UTC);
            if (solarHourSeries is null)
            {
                return null;
            }

            List<GIS.Classes.SurfaceSolarRadiation> result = [];
            foreach (GIS.Classes.SurfaceSolarRadiation surfaceSolarRadiation in surfaceSolarRadiations)
            {
                double[]? shadingFactors = GIS.Convert.ToSystem_Doubles(surfaceSolarRadiation.ShadingFactors);
                GIS.Classes.SurfaceSolarRadiation? surfaceSolarRadiation_Rebuilt = solarHourSeries.SurfaceSolarRadiation(surfaceSolarRadiation.Reference, surfaceSolarRadiation.Area, surfaceSolarRadiation.Normal, surfaceSolarRadiation.SkyVisibility, surfaceSolarRadiation.GroundVisibility, shadingFactors);
                if (surfaceSolarRadiation_Rebuilt is null)
                {
                    return null;
                }

                result.Add(surfaceSolarRadiation_Rebuilt);
            }

            return result;
        }
    }
}
