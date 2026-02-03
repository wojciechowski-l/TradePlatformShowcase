describe('Trade Platform UI (Mocked Backend)', () => {
  const email = 'mock_user@trade.com';
  const password = 'Password123!';

  beforeEach(() => {
    cy.intercept('POST', '/api/auth/register', {
      statusCode: 200,
      body: {}
    }).as('registerUser');

    cy.intercept('POST', '/api/auth/login*', {
      statusCode: 200,
      body: {
        accessToken: 'fake-jwt-token',
        tokenType: 'Bearer',
        expiresIn: 3600,
        refreshToken: 'fake-refresh'
      }
    }).as('loginUser');

    cy.intercept('POST', '/api/transactions', {
      statusCode: 202,
      body: {
        id: 'mock-guid-1234',
        status: 'Pending'
      }
    }).as('submitTransaction');
  });

  it('Completes full flow with mocked backend', () => {
    cy.visit('/');

    cy.register(email, password);
    cy.wait('@registerUser');

    cy.on('window:alert', (text) => {
      expect(text).to.contain('Registration Successful');
    });

    cy.login(email, password);
    cy.wait('@loginUser');

    cy.contains(`Welcome, ${email}`).should('be.visible');

    cy.contains('label', 'Source Account').parent().find('input').type('MOCK-SRC');
    cy.contains('label', 'Target Account').parent().find('input').type('MOCK-TGT');
    cy.contains('label', 'Amount').parent().find('input').type('500');

    cy.contains('button', 'Submit Transaction').click();
    cy.wait('@submitTransaction');

    cy.contains('Success!').should('be.visible');
    cy.contains('Transaction ID: mock-guid-1234').should('be.visible');
  });
});
