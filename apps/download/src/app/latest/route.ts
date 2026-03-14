import { NextResponse } from "next/server";
import { getLatestRelease } from "@/lib/releases";

export const dynamic = "force-dynamic";

export async function GET(request: Request) {
  const latestRelease = await getLatestRelease();

  if (!latestRelease) {
    return new NextResponse("No release is available yet.", {
      status: 404,
      headers: {
        "content-type": "text/plain; charset=utf-8",
      },
    });
  }

  return NextResponse.redirect(new URL(latestRelease.downloadPath, request.url), 307);
}
