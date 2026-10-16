using DiGi.Analytical.Building.Classes;
using DiGi.Analytical.Building.Solar;
using DiGi.Core.Classes;
using DiGi.EPW.Classes;
using DiGi.Geometry.Spatial.Classes;
using DiGi.GIS.WebAPI.UI.Classes;
using DiGi.Solar.Classes;
using System;
using System.Collections.Generic;

namespace DiGi.GIS.WebAPI.UI
{
    public static partial class Create
    {
        /// <summary>
        /// Calculates the annual solar radiation on the external walls and roofs of a building, shaded by the building itself and by its neighbours, over one EPW year.
        /// <para>Composes <see cref="Query.SolarReceiverNormals(BuildingModel?)"/>, <c>ToSolar</c> (the external walls and roofs receive, everything else - the building's other components and every neighbour - only casts shade) and <see cref="SurfaceSolarRadiationResults(ShadingModel?, IDictionary{string, Vector3D}?, EPWFile?, ShadingSolverOptions?, Action{string}?)"/>. A caller that has to refuse a request between those steps (a receiver or caster limit) calls them itself.</para>
        /// </summary>
        /// <param name="buildingModel">The analysed building model, already stamped with its coordinates and time zone (<c>GIS.Analytical.Modify.UpdateBuildingInformation</c>). This value can be null.</param>
        /// <param name="buildingModels_Surrounding">The neighbouring building models casting shade, or null for none. A copy of <paramref name="buildingModel"/> among them is skipped.</param>
        /// <param name="ePWFile">The EPW weather file. This value can be null.</param>
        /// <param name="shadingSolverOptions">The solver options; its time series is replaced by the reference hours. Null uses the defaults.</param>
        /// <returns>One result per receiving surface, or <see langword="null"/> when the model is null or unstamped (no coordinates), has no external envelope, or the EPW file has no usable hour.</returns>
        public static List<SurfaceSolarRadiationResult>? SurfaceSolarRadiationResults(this BuildingModel? buildingModel, IEnumerable<BuildingModel>? buildingModels_Surrounding, EPWFile? ePWFile, ShadingSolverOptions? shadingSolverOptions)
        {
            if (buildingModel is null)
            {
                return null;
            }

            Dictionary<string, Vector3D>? normals = buildingModel.SolarReceiverNormals();
            if (normals is null || normals.Count == 0)
            {
                return null;
            }

            ShadingModel? shadingModel = buildingModel.ToSolar(buildingModels_Surrounding, x => new GuidReference(x).ToString() is string reference && normals.ContainsKey(reference));

            return shadingModel.SurfaceSolarRadiationResults(normals, ePWFile, shadingSolverOptions);
        }

        /// <summary>
        /// Solves a shading model over one EPW year and integrates the irradiation of each receiving surface.
        /// <para>The solve and the aggregation are <see cref="SurfaceSolarRadiations(ShadingModel?, IDictionary{string, Vector3D}?, Weather.Classes.Weather?, ShadingSolverOptions?, Action{string}?)"/>; this answers its summary values only. Every EPW hour with global, direct and diffuse radiation is aggregated at its mid-hour instant in the reference year (<see cref="Query.SolarReferenceDateTime(DateTime)"/>); the shading model is solved for every hour of that year with <see cref="ShadingSolver"/>, which writes its results into <paramref name="shadingModel"/>. For each receiver and hour, the irradiance of the surface is <c>Solar.Create.IrradianceResult</c> on its outward normal, and the power is <c>Solar.Create.SolarPowerResult_ByShadingFactor</c> with the solved shading factor: the shadow blocks the beam component. The sky diffuse and ground-reflected components are scaled by the unblocked share of the surface's sky and ground (<c>Solar.Create.ViewFactorResults</c>, the same projection over fixed hemisphere patches), so a wall covered by a neighbour loses them too. An open-sky twin with factor 0 and nothing blocking the view gives <see cref="SurfaceSolarRadiationResult.IrradiationUnshaded"/>.</para>
        /// <para>A blocked part of the sky or ground contributes nothing: light reflected by the neighbouring facades and roofs is ignored, which underestimates surfaces in narrow street canyons and courtyards (DiGi-Organization/DiGi.Solar#15).</para>
        /// <para>Each receiver's shading factors are read out once, without cloning the stored results, by <c>ShadingModel.TryGetShadingFactors</c> (DiGi-Organization/DiGi.Solar#13, DiGi.GIS.WebAPI.UI#63); a stored result with a NaN area has no factor, so its hour is skipped. The sun direction and the albedo depend only on the hour and are computed once per hour.</para>
        /// <para>Snow cover is never assumed: the served EPW files carry either filler snow depth (IWEC WARSAW reports snow for 8 322 hours) or no albedo at all, so the albedo is the file's own value or the 0.2 default.</para>
        /// <para>A receiver without an outward normal in <paramref name="normals"/>, with no area, or that the solver could not assign (no plane or no triangulation) gets no result. A daytime hour missing from a receiver's results is skipped.</para>
        /// </summary>
        /// <param name="shadingModel">The shading model: the receivers and shading-only casters, with coordinates and a time zone. It receives the solver results. This value can be null.</param>
        /// <param name="normals">The outward unit normal of each receiver, keyed by its reference (<see cref="Query.SolarReceiverNormals(BuildingModel?)"/>). This value can be null.</param>
        /// <param name="ePWFile">The EPW weather file. This value can be null.</param>
        /// <param name="shadingSolverOptions">The solver options; a copy is solved with its time series replaced by the reference hours, so the caller's instance is left untouched. Null uses the defaults.</param>
        /// <param name="log">Receives one line with the direction groups and the solve, view factor and aggregation times, or null for none.</param>
        /// <returns>One result per receiving surface, or <see langword="null"/> when an input is null, the model has no coordinates, the EPW file has no usable hour, or the solver fails.</returns>
        public static List<SurfaceSolarRadiationResult>? SurfaceSolarRadiationResults(this ShadingModel? shadingModel, IDictionary<string, Vector3D>? normals, EPWFile? ePWFile, ShadingSolverOptions? shadingSolverOptions, Action<string>? log = null)
        {
            return SurfaceSolarRadiationResults(shadingModel.SurfaceSolarRadiations(normals, ePWFile, shadingSolverOptions, log));
        }

        /// <summary>
        /// Converts surfaces in their stored form - solved, rebuilt or read back - into the results the solar routes answer and the view colours: the summary values, without the shading factors.
        /// </summary>
        /// <param name="surfaceSolarRadiations">The surfaces.</param>
        /// <returns>One result per non-null surface, in order, or null when <paramref name="surfaceSolarRadiations"/> is null.</returns>
        public static List<SurfaceSolarRadiationResult>? SurfaceSolarRadiationResults(this IEnumerable<GIS.Classes.SurfaceSolarRadiation?>? surfaceSolarRadiations)
        {
            if (surfaceSolarRadiations is null)
            {
                return null;
            }

            List<SurfaceSolarRadiationResult> result = [];
            foreach (GIS.Classes.SurfaceSolarRadiation? surfaceSolarRadiation in surfaceSolarRadiations)
            {
                if (surfaceSolarRadiation is null)
                {
                    continue;
                }

                result.Add(new SurfaceSolarRadiationResult(
                    surfaceSolarRadiation.Reference,
                    surfaceSolarRadiation.Area,
                    surfaceSolarRadiation.Irradiation,
                    surfaceSolarRadiation.Beam,
                    surfaceSolarRadiation.Diffuse,
                    surfaceSolarRadiation.Ground,
                    surfaceSolarRadiation.IrradiationUnshaded,
                    surfaceSolarRadiation.Energy,
                    surfaceSolarRadiation.SkyVisibility,
                    surfaceSolarRadiation.GroundVisibility));
            }

            return result;
        }
    }
}
