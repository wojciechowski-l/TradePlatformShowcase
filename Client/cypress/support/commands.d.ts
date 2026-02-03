/// <reference types="cypress" />

declare global {
  namespace Cypress {
    interface Chainable {
      register(email: string, password: string): Chainable<void>;
      login(email: string, password: string): Chainable<void>;
    }
  }
}

export {};
