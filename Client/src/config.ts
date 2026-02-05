/**
 * Application Configuration
 * * Centralizes environment-specific variables.
 * VITE_API_URL should be set in .env.development or .env.production.
 * Defaults to localhost:5046 for local development if not set.
 */
export const API_BASE_URL = import.meta.env.VITE_API_URL || 'http://localhost:5046';