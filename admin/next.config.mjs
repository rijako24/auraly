/** @type {import('next').NextConfig} */
const nextConfig = {
  distDir: process.env.AURALY_NEXT_DIST_DIR ?? ".next",
  output: "standalone",
  eslint: {
    ignoreDuringBuilds: true,
  },
  typescript: {
    ignoreBuildErrors: false,
  },
};

export default nextConfig;
