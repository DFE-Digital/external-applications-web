import DashboardPage from '../../pages/DashboardPageC:\Users\nsadana\source\repos\external-applications-web\src\DfE.ExternalApplications.CypressTest\Cypress\pages/DashboardPage.js';
import TestLoginPage from '../../pages/TestLoginPage.js/index.js';
describe('Create an Application', () => {


  beforeEach(() => {
    cy.log("Visit the homepage before each test");
    DashboardPage.getHomePage();
  });

  it('should navigate to the dashboard , scroll to the button and  click start button', () => {
    cy.log("Scroll to the start button  on gov.uk page"); 
    DashboardPage.scrollToStartButton();

   cy.log("Clicking the start button on the dashboard");
     DashboardPage.clickStartBtn()
   ;
   cy.wait(10000); // Wait for the page to load after clicking the start button
    // Add assertions or further actions here as needed
    cy.url().should('include', '/TestLogin'); // Example assertion
 });
   


  it('should login with a test user', () => {
    cy.log("Logging in with the test user");
    TestLoginPage.enterUsername();
    TestLoginPage.ContinueBtn();
    // Add assertions or further actions here as needed
   // cy.url().should('include', '/task-list'); // Example assertion
  });
});
