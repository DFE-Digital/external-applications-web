import GovUKPage from '../../pages/GovUKPage.js';
import TestLoginPage from '../../pages/TestLoginPage.js';
import Dashboardpage from '../../pages/Dashboardpage.js';
describe('Create an Application', () => {


  beforeEach(() => {
    cy.log("Visit the homepage before each test");
    GovUKPage.getHomePage();
  });

  it('should navigate to the Gov.uk , scroll to the button and  click start button', () => {
    cy.log("Scroll to the start button  on Gov.uk page"); 
    GovUKPage.scrollToStartButton();

   cy.log("Clicking the start button on the dashboard");
     GovUKPage.clickStartBtn() ;
   // Wait for the page to load after clicking the start button

   cy.wait(1000);
    // Add assertions or further actions here as needed
    cy.url().should('include', '/TestLogin'); // Example assertion


    cy.log("Logging in with the test user");
    TestLoginPage.enterUsername();
    TestLoginPage.ContinueBtn();
    // Add assertions or further actions here as needed


    //Fo to Dashboard and click Start New Application
    //cy.url().should('include', '/dashboard'); 
    cy.log("Clicking the Start New Application button");
    Dashboardpage.clickStartBtn()


 });
   

  
    
 
});
