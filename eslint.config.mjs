import js from '@eslint/js';
import reactHooks from 'eslint-plugin-react-hooks';
import globals from 'globals';
import tseslint from 'typescript-eslint';

/**
 * Configuração de lint das aplicações web.
 *
 * As regras com tipo (`recommendedTypeChecked`) exigem que o ESLint carregue o
 * projeto TypeScript. É mais lento, e é o que permite pegar a classe de erro que
 * mais aparece em código assíncrono: promessa criada e não aguardada.
 */
export default tseslint.config(
  {
    ignores: [
      '**/dist/**',
      '**/node_modules/**',
      '**/bin/**',
      '**/obj/**',
      '**/coverage/**',
    ],
  },

  js.configs.recommended,
  ...tseslint.configs.recommendedTypeChecked,
  ...tseslint.configs.stylisticTypeChecked,

  {
    languageOptions: {
      parserOptions: {
        projectService: true,
        tsconfigRootDir: import.meta.dirname,
      },
      globals: {
        ...globals.browser,
      },
    },
  },

  {
    files: ['apps/*/src/**/*.{ts,tsx}'],
    plugins: { 'react-hooks': reactHooks },
    rules: {
      ...reactHooks.configs.recommended.rules,

      // Nomes do domínio são em português; a regra de naming em inglês não se aplica.
      '@typescript-eslint/naming-convention': 'off',

      // Deixar uma promessa sem await é a origem silenciosa de erro não tratado.
      '@typescript-eslint/no-floating-promises': 'error',

      // `any` apaga exatamente a garantia pela qual o TypeScript está aqui.
      '@typescript-eslint/no-explicit-any': 'error',

      '@typescript-eslint/consistent-type-imports': [
        'error',
        { prefer: 'type-imports', fixStyle: 'separate-type-imports' },
      ],
    },
  },

  {
    files: ['apps/*/src/**/*.test.{ts,tsx}'],
    rules: {
      // Asserção de teste frequentemente usa valor não tipado vindo de um dublê.
      '@typescript-eslint/no-unsafe-assignment': 'off',
      '@typescript-eslint/no-unsafe-member-access': 'off',
    },
  },

  {
    files: ['apps/*/vite.config.ts', 'apps/*/vitest.setup.ts'],
    languageOptions: {
      globals: { ...globals.node },
    },
  },

  {
    // Este próprio arquivo não pertence a nenhum tsconfig das aplicações, então as
    // regras que exigem informação de tipo não têm como ser aplicadas a ele.
    files: ['eslint.config.mjs'],
    ...tseslint.configs.disableTypeChecked,
    languageOptions: {
      // `projectService: false` precisa ser explícito: sem isto o parser herda a
      // configuração do bloco anterior e tenta achar este arquivo num tsconfig
      // das aplicações, onde ele não está nem deveria estar.
      parserOptions: { projectService: false, project: null },
      globals: { ...globals.node },
    },
  },
);
