/** @type {import('next').NextConfig} */
const nextConfig = {
  distDir: process.env.AURALY_NEXT_DIST_DIR ?? ".next",
  output: "standalone",
  images: {
    // The installed desktop payload lives under Program Files. Runtime image
    // optimization must not try to create .next/cache there as a normal user.
    unoptimized: process.env.AURALY_DESKTOP_BUILD === "1",
  },
  typescript: {
    ignoreBuildErrors: false,
  },
};

export default nextConfig;
