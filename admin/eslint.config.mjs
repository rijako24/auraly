import nextVitals from "eslint-config-next/core-web-vitals";
import nextTypeScript from "eslint-config-next/typescript";

export default [
  ...nextVitals,
  ...nextTypeScript,
  {
    rules: {
      "react-hooks/set-state-in-effect": "warn",
      "@typescript-eslint/no-empty-object-type": "warn",
      "@typescript-eslint/no-require-imports": "warn",
    },
  },
  {
    ignores: [
      ".next/**",
      "out/**",
      "build/**",
      "next-env.d.ts",
    ],
  },
];
