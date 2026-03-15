import { NextResponse } from "next/server";
import { getDownloadsUrl, getLatestRelease } from "@/lib/releases";

export const dynamic = "force-dynamic";

function buildAbsoluteDownloadUrl(downloadPath: string, requestUrl: string): string {
  try {
    const configuredBase = getDownloadsUrl();
    return new URL(downloadPath, configuredBase).toString();
  } catch {
    return new URL(downloadPath, requestUrl).toString();
  }
}

export async function GET(request: Request) {
  const latestRelease = await getLatestRelease();
  if (!latestRelease) {
    return NextResponse.json(
      {
        ok: false,
        error: "no_release_available",
        latest: null,
      },
      {
        status: 404,
        headers: {
          "cache-control": "no-store",
        },
      },
    );
  }

  const downloadUrl = buildAbsoluteDownloadUrl(latestRelease.downloadPath, request.url);

  return NextResponse.json(
    {
      ok: true,
      latest: {
        version: latestRelease.version,
        fileName: latestRelease.fileName,
        downloadPath: latestRelease.downloadPath,
        downloadUrl,
        size: latestRelease.size,
        sha256: latestRelease.sha256,
        uploadedAtIso: latestRelease.uploadedAt.toISOString(),
      },
    },
    {
      headers: {
        "cache-control": "public, max-age=60, stale-while-revalidate=300",
      },
    },
  );
}
