import type { Metadata } from "next";

const socialImagePath = "/images/Glitch%20Banner.png";

type PageMetadataInput = {
  title: string;
  description: string;
  path: string;
};

export function buildPageMetadata({ title, description, path }: PageMetadataInput): Metadata {
  return {
    title,
    description,
    alternates: {
      canonical: path,
    },
    openGraph: {
      title,
      description,
      url: path,
      type: "website",
      siteName: "Glitch",
      locale: "en_US",
      images: [
        {
          url: socialImagePath,
          alt: "Glitch trading assistant banner",
        },
      ],
    },
    twitter: {
      card: "summary_large_image",
      title,
      description,
      images: [socialImagePath],
    },
  };
}
