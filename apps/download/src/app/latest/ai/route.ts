import { NextResponse } from "next/server";
import { buildAbsoluteDownloadUrl, getLatestRelease } from "@/lib/releases";

export const dynamic = "force-dynamic";

export async function GET(request: Request) {
  const latestRelease = await getLatestRelease("ai");

  if (!latestRelease) {
    return new NextResponse("No AI release is available yet.", {
      status: 404,
      headers: {
        "content-type": "text/plain; charset=utf-8",
      },
    });
  }

  const targetUrl = buildAbsoluteDownloadUrl(latestRelease.downloadPath, request.url);
  return NextResponse.redirect(targetUrl, 307);
}
