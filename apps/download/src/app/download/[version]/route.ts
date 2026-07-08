import { NextResponse } from "next/server";
import { buildAbsoluteDownloadUrl, getReleaseBySlug } from "@/lib/releases";

export const dynamic = "force-dynamic";

type Props = {
  params: Promise<{
    version: string;
  }>;
};

export async function GET(request: Request, { params }: Props) {
  const { version } = await params;
  const release = await getReleaseBySlug(version);

  if (!release) {
    return new NextResponse("Requested release was not found.", {
      status: 404,
      headers: {
        "content-type": "text/plain; charset=utf-8",
      },
    });
  }

  const targetUrl = buildAbsoluteDownloadUrl(release.downloadPath, request.url);
  return NextResponse.redirect(targetUrl, 307);
}
