import { NextResponse } from "next/server";
import {
  buildAbsoluteDownloadUrl,
  getLatestRelease,
  type ReleaseEdition,
} from "@/lib/releases";

export const dynamic = "force-dynamic";

export async function GET(request: Request) {
  const editionValue = new URL(request.url).searchParams.get("edition")?.trim().toLowerCase();
  if (editionValue && editionValue !== "standard" && editionValue !== "ai") {
    return NextResponse.json(
      {
        ok: false,
        error: "invalid_edition",
        latest: null,
      },
      {
        status: 400,
        headers: {
          "cache-control": "no-store",
        },
      },
    );
  }

  const edition: ReleaseEdition = editionValue === "ai" ? "ai" : "standard";
  const latestRelease = await getLatestRelease(edition);
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
        edition: latestRelease.edition,
        status: latestRelease.status,
        fileName: latestRelease.fileName,
        downloadPath: latestRelease.downloadPath,
        downloadUrl,
        size: latestRelease.size,
        sha256: latestRelease.sha256,
        sourceCommit: latestRelease.sourceCommit,
        hermesProfileVersion: latestRelease.hermesProfileVersion,
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
