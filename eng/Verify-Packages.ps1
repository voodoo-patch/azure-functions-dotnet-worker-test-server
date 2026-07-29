[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$PackageDirectory,

    [Parameter(Mandatory)]
    [string]$RepositoryUrl,

    [string]$PackageVersion
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.IO.Compression.FileSystem

$expectedPackageIds = @(
    'Azure.Functions.Worker.TestServer',
    'Azure.Functions.Worker.TestServer.Abstractions'
)

$packages = @(Get-ChildItem -Path $PackageDirectory -Filter '*.nupkg' -File)
if ($packages.Count -ne $expectedPackageIds.Count) {
    throw "Expected $($expectedPackageIds.Count) NuGet packages in '$PackageDirectory', found $($packages.Count)."
}

foreach ($packageId in $expectedPackageIds) {
    $packagePattern = '^{0}\.[0-9]' -f [regex]::Escape($packageId)
    $package = $packages | Where-Object { $_.Name -match $packagePattern }
    if ($null -eq $package) {
        throw "Package '$packageId' was not found in '$PackageDirectory'."
    }

    $archive = [System.IO.Compression.ZipFile]::OpenRead($package.FullName)
    try {
        $nuspecEntry = $archive.Entries | Where-Object { $_.FullName.EndsWith('.nuspec', [StringComparison]::OrdinalIgnoreCase) }
        if ($null -eq $nuspecEntry) {
            throw "Package '$($package.Name)' does not contain a nuspec file."
        }

        $reader = [System.IO.StreamReader]::new($nuspecEntry.Open())
        try {
            [xml]$nuspec = $reader.ReadToEnd()
        }
        finally {
            $reader.Dispose()
        }

        $namespaceManager = [System.Xml.XmlNamespaceManager]::new($nuspec.NameTable)
        $namespaceManager.AddNamespace('n', 'http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd')
        $metadata = $nuspec.SelectSingleNode('/n:package/n:metadata', $namespaceManager)

        $actualPackageId = $metadata.SelectSingleNode('n:id', $namespaceManager).InnerText
        if ($actualPackageId -ne $packageId) {
            throw "Package '$($package.Name)' has package ID '$actualPackageId' instead of '$packageId'."
        }

        $license = $metadata.SelectSingleNode('n:license', $namespaceManager)
        if ($null -eq $license -or
            $license.GetAttribute('type') -ne 'expression' -or
            $license.InnerText -ne 'Apache-2.0') {
            throw "Package '$packageId' must use the Apache-2.0 license expression."
        }

        $repository = $metadata.SelectSingleNode('n:repository', $namespaceManager)
        if ($null -eq $repository -or
            $repository.GetAttribute('type') -ne 'git' -or
            $repository.GetAttribute('url') -ne $RepositoryUrl) {
            throw "Package '$packageId' has incorrect repository metadata."
        }

        $actualPackageVersion = $metadata.SelectSingleNode('n:version', $namespaceManager).InnerText
        if ($PackageVersion -and $actualPackageVersion -ne $PackageVersion) {
            throw "Package '$packageId' has version '$actualPackageVersion' instead of '$PackageVersion'."
        }

        if (-not ($archive.Entries.FullName -contains 'README.md')) {
            throw "Package '$packageId' does not include README.md."
        }

        if (-not ($archive.Entries.FullName | Where-Object { $_ -match '^lib/net10\.0/.+\.dll$' })) {
            throw "Package '$packageId' does not include a net10.0 library assembly."
        }

        if ($packageId -eq 'Azure.Functions.Worker.TestServer') {
            $abstractionsDependency = $metadata.SelectSingleNode(
                './/n:dependency[@id="Azure.Functions.Worker.TestServer.Abstractions"]',
                $namespaceManager)
            if ($null -eq $abstractionsDependency) {
                throw "Package '$packageId' must depend on Azure.Functions.Worker.TestServer.Abstractions."
            }

            if ($abstractionsDependency.GetAttribute('version') -ne $actualPackageVersion) {
                throw "Package '$packageId' must depend on the matching abstractions package version."
            }
        }
    }
    finally {
        $archive.Dispose()
    }
}

$symbols = @(Get-ChildItem -Path $PackageDirectory -Filter '*.snupkg' -File)
if ($symbols.Count -ne $expectedPackageIds.Count) {
    throw "Expected $($expectedPackageIds.Count) symbol packages in '$PackageDirectory', found $($symbols.Count)."
}

foreach ($symbol in $symbols) {
    $archive = [System.IO.Compression.ZipFile]::OpenRead($symbol.FullName)
    try {
        if (-not ($archive.Entries.FullName | Where-Object { $_.EndsWith('.pdb', [StringComparison]::OrdinalIgnoreCase) })) {
            throw "Symbol package '$($symbol.Name)' does not include a PDB."
        }
    }
    finally {
        $archive.Dispose()
    }
}
