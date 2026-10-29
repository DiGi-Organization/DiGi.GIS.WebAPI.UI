#### [DiGi\.GIS\.WebAPI\.UI](DiGi.GIS.WebAPI.UI.Overview.md 'DiGi\.GIS\.WebAPI\.UI\.Overview')

## DiGi\.GIS\.WebAPI\.UI\.Enums Namespace
### Enums

<a name='DiGi.GIS.WebAPI.UI.Enums.RegulatedBuildingCategory'></a>

## RegulatedBuildingCategory Enum

Specifies the building category that decides which limits of a technical\-conditions regulation apply to a building \(DiGi\-Organization/DiGi\.GIS\.WebAPI\.UI\#82\)\.

The 1997, 2002 and 2008 regulations set limits per category: single-family building rows, residential rows for every residential building, and public building rows, which the panel also shows for any other non-residential building.

```csharp
public enum RegulatedBuildingCategory
```
### Fields

<a name='DiGi.GIS.WebAPI.UI.Enums.RegulatedBuildingCategory.Undefined'></a>

`Undefined` 0

The category is not known \(no footprint was found\); every limit is shown\.

<a name='DiGi.GIS.WebAPI.UI.Enums.RegulatedBuildingCategory.SingleFamily'></a>

`SingleFamily` 1

A single\-family residential building\.

<a name='DiGi.GIS.WebAPI.UI.Enums.RegulatedBuildingCategory.MultiFamily'></a>

`MultiFamily` 2

A multi\-family residential building; the single\-family limits do not apply to it\.

<a name='DiGi.GIS.WebAPI.UI.Enums.RegulatedBuildingCategory.Residential'></a>

`Residential` 3

A residential building that is neither marked single\-family nor multi\-family, or is marked both; every residential limit is shown\.

<a name='DiGi.GIS.WebAPI.UI.Enums.RegulatedBuildingCategory.NonResidential'></a>

`NonResidential` 4

A non\-residential building; the public building limits are shown\.

<a name='DiGi.GIS.WebAPI.UI.Enums.SolarJobStatus'></a>

## SolarJobStatus Enum

The state of a background solar radiation job \([SolarJob](DiGi.GIS.WebAPI.UI.Classes.md#DiGi.GIS.WebAPI.UI.Classes.SolarJob 'DiGi\.GIS\.WebAPI\.UI\.Classes\.SolarJob')\)\.

```csharp
public enum SolarJobStatus
```
### Fields

<a name='DiGi.GIS.WebAPI.UI.Enums.SolarJobStatus.Queued'></a>

`Queued` 0

Waiting in the queue, or for the solve gate the job shares with the synchronous requests\.

<a name='DiGi.GIS.WebAPI.UI.Enums.SolarJobStatus.Running'></a>

`Running` 1

Being calculated\.

<a name='DiGi.GIS.WebAPI.UI.Enums.SolarJobStatus.Completed'></a>

`Completed` 2

Calculated; its results can be fetched until the job expires\.

<a name='DiGi.GIS.WebAPI.UI.Enums.SolarJobStatus.Failed'></a>

`Failed` 3

The calculation threw or produced no result; the job carries the error text\.

<a name='DiGi.GIS.WebAPI.UI.Enums.SolarJobStatus.Cancelled'></a>

`Cancelled` 4

Cancelled by the client: a queued job is never calculated, and the results of a running one are discarded\.

<a name='DiGi.GIS.WebAPI.UI.Enums.SolarRadiationSource'></a>

## SolarRadiationSource Enum

Specifies where the solar radiation a response shows came from\.

```csharp
public enum SolarRadiationSource
```
### Fields

<a name='DiGi.GIS.WebAPI.UI.Enums.SolarRadiationSource.Solved'></a>

`Solved` 0

Solved for this request: neighbours fetched, shading model built, shading solver run\.

<a name='DiGi.GIS.WebAPI.UI.Enums.SolarRadiationSource.Stored'></a>

`Stored` 1

Loaded from a stored result solved for the same building model, radius, solver version and weather source; nothing was solved\.

<a name='DiGi.GIS.WebAPI.UI.Enums.SolarRadiationSource.Rebuilt'></a>

`Rebuilt` 2

Rebuilt from the shading factors of a stored result for another weather source; no neighbours, no shading model, no solve\.