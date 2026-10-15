using DiGi.Core.IO.Table.Classes;
using DiGi.GIS.WebAPI.UI.Classes;
using DiGi.GIS.WebAPI.UI.ViewModels;
using DiGi.Typology.Visual.Classes;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DiGi.GIS.WebAPI.UI
{
    public static partial class Create
    {
        /// <summary>
        /// Chooses the representative of a typology node from its buildings' data, and describes it for the area view.
        /// <para>The parameters are the definition's levels. A level with the unique-value rule is compared as a category, and any other level (a range rule) as a number (<see cref="Query.Representative"/>). A level whose column the table does not carry is skipped. The floor area and storeys columns give the synthetic archetype's dimensions as the node's medians, and the storeys median is rounded half away from zero, never below 1.</para>
        /// <para>The table is read through its live enumeration, never through <c>Rows</c>, which clones every row it hands out.</para>
        /// </summary>
        /// <param name="table">The building data of the node's buildings, carrying the definition's columns plus Reference, County Id, Database Id, Floor area and Storeys. This value can be null.</param>
        /// <param name="typologyDefinitionParameter">The definition the node was solved with. This value can be null.</param>
        /// <param name="columns">The live column catalog, resolving each level's unique identifier to the column name the table is keyed by. This value can be null.</param>
        /// <returns>The view model, or <see langword="null"/> when an input is missing, the table carries no reference column or no row, or no building is complete enough to be chosen.</returns>
        public static ViewModels.TypologyRepresentativeViewModel? TypologyRepresentativeViewModel(this Table? table, TypologyDefinitionParameter? typologyDefinitionParameter, IEnumerable<DiGi.PostgreSQL.Table.Classes.Column>? columns)
        {
            if (table is null || typologyDefinitionParameter?.Levels is null)
            {
                return null;
            }

            Dictionary<string, DiGi.PostgreSQL.Table.Classes.Column>? columnDictionary = Query.ColumnDictionary(columns);
            if (columnDictionary is null)
            {
                return null;
            }

            int index_Reference = table.GetColumnIndex(Constants.BuildingData.ReferenceName);
            if (index_Reference == -1)
            {
                return null;
            }

            int index_CountyId = table.GetColumnIndex(Constants.BuildingData.CountyIdName);
            int index_Id = table.GetColumnIndex(Constants.BuildingData.DatabaseIdName);
            int index_FloorArea = table.GetColumnIndex(Constants.BuildingData.FloorAreaName);
            int index_Storeys = table.GetColumnIndex(Constants.BuildingData.StoreysName);

            // The levels the table carries: display name, table index and whether the level is a category.
            List<(string Name, int Index, bool IsCategorical)> levels = [];
            foreach (TypologyDefinitionLevelParameter level in typologyDefinitionParameter.Levels)
            {
                if (level?.UniqueId is null || !columnDictionary.TryGetValue(level.UniqueId, out DiGi.PostgreSQL.Table.Classes.Column? column) || column?.Name is null)
                {
                    continue;
                }

                int index = table.GetColumnIndex(column.Name);
                if (index == -1)
                {
                    continue;
                }

                levels.Add((level.Name ?? column.Name, index, level.RuleType == nameof(VisualUniqueValueFilterRule)));
            }

            List<string?> references = [];
            List<int> countyIds = [];
            List<long> ids = [];
            List<double?> floorAreas = [];
            List<double?> storeys = [];
            List<List<double?>> numericColumns = [.. levels.Select(_ => new List<double?>())];
            List<List<string?>> categoricalColumns = [.. levels.Select(_ => new List<string?>())];

            foreach (Row row in table)
            {
                if (row[index_Reference] is not string reference)
                {
                    continue;
                }

                references.Add(reference);
                countyIds.Add(index_CountyId == -1 ? 0 : (int)(ToDouble(row[index_CountyId]) ?? 0));
                ids.Add(index_Id == -1 ? 0 : (long)(ToDouble(row[index_Id]) ?? 0));
                floorAreas.Add(index_FloorArea == -1 ? null : ToDouble(row[index_FloorArea]));
                storeys.Add(index_Storeys == -1 ? null : ToDouble(row[index_Storeys]));

                for (int i = 0; i < levels.Count; i++)
                {
                    object? value = row[levels[i].Index];
                    if (levels[i].IsCategorical)
                    {
                        categoricalColumns[i].Add(value is null ? null : System.Convert.ToString(value, CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        numericColumns[i].Add(ToDouble(value));
                    }
                }
            }

            if (references.Count == 0)
            {
                return null;
            }

            List<IReadOnlyList<double?>> numericColumns_Used = [];
            List<IReadOnlyList<string?>> categoricalColumns_Used = [];
            for (int i = 0; i < levels.Count; i++)
            {
                if (levels[i].IsCategorical)
                {
                    categoricalColumns_Used.Add(categoricalColumns[i]);
                }
                else
                {
                    numericColumns_Used.Add(numericColumns[i]);
                }
            }

            Representative? representative = Query.Representative(references, numericColumns_Used, categoricalColumns_Used);
            if (representative is null)
            {
                return null;
            }

            int index_Representative = representative.Index;

            List<TypologyRepresentativeParameterViewModel> parameters = [];
            for (int i = 0; i < levels.Count; i++)
            {
                if (levels[i].IsCategorical)
                {
                    string? modal = categoricalColumns[i].Where(x => x is not null).GroupBy(x => x!, StringComparer.Ordinal).OrderByDescending(x => x.Count()).ThenBy(x => x.Key, StringComparer.Ordinal).Select(x => x.Key).FirstOrDefault();
                    parameters.Add(new TypologyRepresentativeParameterViewModel(levels[i].Name, false, categoricalColumns[i][index_Representative], modal));
                }
                else
                {
                    double median = Values(numericColumns[i]).Quantile(0.5);
                    parameters.Add(new TypologyRepresentativeParameterViewModel(levels[i].Name, true, Format(numericColumns[i][index_Representative]), Format(median)));
                }
            }

            double floorArea = Values(floorAreas).Quantile(0.5);
            double storeys_Median = Values(storeys).Quantile(0.5);

            double? syntheticFloorArea = double.IsFinite(floorArea) && floorArea > 0 ? floorArea : null;
            int? syntheticStoreys = double.IsFinite(storeys_Median) ? System.Math.Max(1, (int)System.Math.Round(storeys_Median, MidpointRounding.AwayFromZero)) : null;

            return new ViewModels.TypologyRepresentativeViewModel(
                references[index_Representative]!,
                ids[index_Representative],
                countyIds[index_Representative],
                references.Count,
                representative,
                parameters,
                syntheticFloorArea,
                syntheticStoreys);

            // The table types every cell by its column's declared type; any numeric width converts.
            static double? ToDouble(object? value)
            {
                return value switch
                {
                    double @double => @double,
                    float @float => @float,
                    decimal @decimal => (double)@decimal,
                    long @long => @long,
                    int @int => @int,
                    short @short => @short,
                    ushort @ushort => @ushort,
                    uint @uint => @uint,
                    ulong @ulong => @ulong,
                    byte @byte => @byte,
                    sbyte @sbyte => @sbyte,
                    _ => null
                };
            }

            static IEnumerable<double> Values(IEnumerable<double?> values)
            {
                return values.Where(x => x.HasValue).Select(x => x!.Value);
            }

            static string? Format(double? value)
            {
                return value.HasValue && double.IsFinite(value.Value) ? value.Value.ToString("0.##", CultureInfo.InvariantCulture) : null;
            }
        }
    }
}
