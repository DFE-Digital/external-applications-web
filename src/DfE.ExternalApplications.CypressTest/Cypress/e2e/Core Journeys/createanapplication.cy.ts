import GovUKPage from '../../pages/GovUKPage';
import TestLoginPage from '../../pages/TestLoginPage';
import Dashboardpage from '../../pages/Dashboardpage';
import { Logger } from '../../Common/logger';
import '../../support/commands';
import Contributors from '../../pages/ContributorsPage';
// Import the necessary classes and methods
describe('Create an Application', () => {


 beforeEach(() => {
        cy.login();
        cy.executeAccessibilityTests();
    });

  it('should navigate to the Gov.uk , scroll to the button and  click start button', () => {
    Logger.log("Scroll to the start button  on Gov.uk page"); 
    GovUKPage.scrollToStartButton();

   Logger.log("Clicking the start button on the dashboard");
     GovUKPage.clickStartBtn() ;
   // Wait for the page to load after clicking the start button

    // Add assertions or further actions here as needed
    Logger.log("Verify if redirected to TestLogin page"); 
    cy.url().should('include', '/TestLogin'); // Example assertion

    Logger.log("Logging in with the test user");
    TestLoginPage.enterUsername(Cypress.env('username'));
     Logger.log("Clicking the Continue button on the TestLogin page");
    TestLoginPage.ContinueBtn();
    // Add assertions or further actions here as needed

    //  assertion to check if redirected to dashboard
    Logger.log("Verify if redirected to Dashboard page");
    cy.url().should('include', '/dashboard'); 
    //Go to Dashboard and click Start New Application
    
    Logger.log("Clicking the Start New Application button");
    Dashboardpage.clickStartBtn();
    //Proceed to Application from Contributors Page
    
    Logger.log("Verify if redirected to Dashboard page");
    cy.url().should('include', '/contributors'); 
Logger.log("Clicking the Proceed button on Contributors page");
    Contributors.ClickProceedBtn();


 });
   

  
    
 
});
