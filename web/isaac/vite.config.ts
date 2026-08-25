import { defineConfig } from 'vite';

export default defineConfig({
  build: {
    // The viewer needs the NVIDIA SDK before its first connection.  Keep one local
    // bundle and treat 700 KiB as an explicit regression budget.
    chunkSizeWarningLimit: 700,
  },
});
