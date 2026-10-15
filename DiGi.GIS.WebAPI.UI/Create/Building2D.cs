using DiGi.Geometry.Planar;
using DiGi.Geometry.Planar.Classes;
using System;

namespace DiGi.GIS.WebAPI.UI
{
    public static partial class Create
    {
        /// <summary>
        /// Creates a synthetic representative of a typology node from its real representative: the real footprint shape, scaled about its centroid to the given floor area, with the given number of storeys.
        /// <para>The result is a hybrid archetype - a shape that exists in the node, at the node's typical size. Only floor area and storeys can be carried by the geometry; every other parameter of the node is reported as a median beside it, not built in. The phase and functions are copied from the source, the reference too, so the archetype can be traced back to the building it was derived from; the identifier is new, because it is not that building.</para>
        /// <para>The footprint is scaled by <c>sqrt(floorArea / area)</c>, which keeps its proportions and any courtyard (internal edges scale with it). The source is not modified.</para>
        /// </summary>
        /// <param name="building2D">The real representative the shape is taken from. This value can be null.</param>
        /// <param name="floorArea">The floor area to scale the footprint to, in square metres.</param>
        /// <param name="storeys">The number of storeys of the archetype.</param>
        /// <returns>The synthetic building, or <see langword="null"/> when the source has no footprint, its area or <paramref name="floorArea"/> is not a positive finite number, or <paramref name="storeys"/> is 0.</returns>
        public static GIS.Classes.Building2D? Building2D(this GIS.Classes.Building2D? building2D, double floorArea, ushort storeys)
        {
            if (building2D?.PolygonalFace2D is null || !double.IsFinite(floorArea) || floorArea <= 0 || storeys == 0)
            {
                return null;
            }

            PolygonalFace2D polygonalFace2D = new(building2D.PolygonalFace2D);

            double area = polygonalFace2D.GetArea();
            if (!double.IsFinite(area) || area <= 0)
            {
                return null;
            }

            Point2D? centroid = polygonalFace2D.Centroid();
            if (centroid is null)
            {
                return null;
            }

            double factor = System.Math.Sqrt(floorArea / area);

            // Scale(x, y) rather than Scale(factor): the uniform overload also sets matrix [2, 2] to the factor, which
            // multiplies the composed translation by it and scales about the origin instead of the centroid.
            Transform2D? transform2D = Geometry.Planar.Create.Transform2D.Translation(centroid.X, centroid.Y) * Geometry.Planar.Create.Transform2D.Scale(factor, factor) * Geometry.Planar.Create.Transform2D.Translation(-centroid.X, -centroid.Y);
            if (transform2D is null || !polygonalFace2D.Transform(transform2D))
            {
                return null;
            }

            return new GIS.Classes.Building2D(Guid.NewGuid(), building2D.Reference, polygonalFace2D, storeys, building2D.BuildingPhase, building2D.BuildingGeneralFunction, building2D.BuildingSpecificFunctions);
        }
    }
}
