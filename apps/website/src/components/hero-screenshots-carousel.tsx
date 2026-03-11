"use client";

import Image from "next/image";
import { Swiper, SwiperSlide } from "swiper/react";
import { Autoplay, EffectCoverflow, Keyboard } from "swiper/modules";
import "swiper/css";
import "swiper/css/effect-coverflow";

type ScreenshotSlide = {
  id: string;
  src: string;
  alt: string;
  width: number;
  height: number;
};

const screenshotSlides: ScreenshotSlide[] = [
  {
    id: "01",
    src: "/images/swiper/01.jpg",
    alt: "Glitch screenshot 01",
    width: 1635,
    height: 1200,
  },
  {
    id: "02",
    src: "/images/swiper/02.jpg",
    alt: "Glitch screenshot 02",
    width: 1639,
    height: 1204,
  },
  {
    id: "03",
    src: "/images/swiper/03.jpg",
    alt: "Glitch screenshot 03",
    width: 1635,
    height: 1200,
  },
  {
    id: "04",
    src: "/images/swiper/04.jpg",
    alt: "Glitch screenshot 04",
    width: 1639,
    height: 1204,
  },
  {
    id: "05",
    src: "/images/swiper/05.jpg",
    alt: "Glitch screenshot 05",
    width: 1639,
    height: 1204,
  },
  {
    id: "06",
    src: "/images/swiper/06.jpg",
    alt: "Glitch screenshot 06",
    width: 1639,
    height: 1204,
  },
  {
    id: "07",
    src: "/images/swiper/07.jpg",
    alt: "Glitch screenshot 07",
    width: 1639,
    height: 1204,
  },
  {
    id: "08",
    src: "/images/swiper/08.jpg",
    alt: "Glitch screenshot 08",
    width: 1639,
    height: 1204,
  },
];

export function HeroScreenshotsCarousel() {
  return (
    <div className="h-auto w-full overflow-hidden">
      <div className="glitch-screenshots-stage relative left-1/2 w-screen -translate-x-1/2">
        <Swiper
          modules={[Autoplay, EffectCoverflow, Keyboard]}
          className="glitch-screenshots-swiper"
          centeredSlides
          effect="coverflow"
          autoplay={{
            delay: 4500,
            disableOnInteraction: false,
          }}
          grabCursor
          keyboard={{ enabled: true }}
          threshold={14}
          touchRatio={0.55}
          longSwipesRatio={0.3}
          longSwipesMs={360}
          slideToClickedSlide
          initialSlide={1}
          loop
          speed={900}
          slidesPerView="auto"
          spaceBetween={-140}
          breakpoints={{
            768: {
              spaceBetween: -220,
            },
            1200: {
              spaceBetween: -300,
            },
          }}
          coverflowEffect={{
            rotate: 12,
            stretch: -280,
            depth: 240,
            scale: 0.8,
            modifier: 1.12,
            slideShadows: false,
          }}
        >
          {screenshotSlides.map((slide) => (
            <SwiperSlide key={slide.id}>
              <div className="glitch-screenshot-shell">
                <Image
                  src={slide.src}
                  alt={slide.alt}
                  width={slide.width}
                  height={slide.height}
                  className="glitch-screenshot-image select-none"
                  unoptimized
                  priority={slide.id === "05"}
                  draggable={false}
                />
              </div>
            </SwiperSlide>
          ))}
        </Swiper>
      </div>
    </div>
  );
}
