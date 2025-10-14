// Import the necessary classes and methods
import GovUKPage from '../../pages/GovUKPage';
import TestLoginPage from '../../pages/TestLoginPage';
import Dashboardpage from '../../pages/Dashboardpage';
import { Logger } from '../../Common/logger';
import '../../support/commands';
import Contributors from '../../pages/ContributorsPage';
import ReasonsAndBenefits from '../../pages/TaskListPages/ReasonsAndBenefitsPage'; 
import Academies from '../../pages/TaskListPages/DetailsofAcademies';
import OutgoingTrust from '../../pages/TaskListPages/OutgoingTrust';
import Risks from '../../pages/TaskListPages/Risks';
import IncomingTrust from '../../pages/TaskListPages/IncomingTrustDetails';
import ReasonsAndBenefitsIncoming from '../../pages/TaskListPages/ReasonsAndBenefitsIncoming';
import HighQualityInclusiveEducation from '../../pages/TaskListPages/HighQualityInclusiveEducation';
import SchoolImprovement from '../../pages/TaskListPages/SchoolImprovement';
import FinanceAndOperations from '../../pages/TaskListPages/FinanceAndOperations';
import Leadership from '../../pages/TaskListPages/Leadership';
import Members from '../../pages/TaskListPages/Members';
import Trustees from '../../pages/TaskListPages/Trustees';
import GovernanceStructure from '../../pages/TaskListPages/GovernanceStructure';
import Declaration from '../../pages/TaskListPages/Declaration';


describe('Create an Application', () => {

 beforeEach(() => {
        cy.login();      
        cy.executeAccessibilityTests();
    });

  it('should navigate to the Gov.uk , scroll to the button and  click start button', () => {
    Logger.log("Scroll to the start button  on Gov.uk page"); 
  //  GovUKPage.scrollToStartButton();

   Logger.log("Clicking the start button on the dashboard");
     GovUKPage.clickStartBtn() ;
     
   
    Logger.log("Logging in with the test user");
     TestLoginPage.enterUsername(Cypress.env('username'));
      //Click Continue button
      TestLoginPage.clickContinue();
    // Add assertions or further actions here as needed

    //  assertion to check if redirected to dashboard
    Logger.log("Verify if redirected to Dashboard page");
    cy.url().should('include', '/dashboard'); 
    //Go to Dashboard and click Start New Application
    
    Logger.log("Clicking the Start New Application button");
    Dashboardpage.clickStartBtn();
    
    Logger.log("Verify if redirected to Dashboard page");
    cy.url().should('include', '/contributors'); 

    // Add a contributor 
    Logger.log("Adding a contributor");
    Contributors.addContributor();
    // Verify if the navigated to Contributors Page
    Logger.log("Verify if redirected to Contributors page");
    cy.url().should('include', '/contributors'); 

    // Verify if the contributor is added
    Logger.log("Verifying if the contributor is added");
    Contributors.verifyNewContributor();
    Logger.log("Clicking the Proceed button on Contributors page");
    Contributors.ClickProceedBtn();

    // Verify if the navigated to Task List Page
   Logger.log("Verify if redirected to Task List page");
   cy.url().should('include', '/applications/');
   // Click on the Invite Contributors button from Task List page
    Contributors.inviteContributors();
    Logger.log("Clicking the Proceed button on Contributors page");
    Contributors.ClickProceedBtn();
    // Verify if the navigated to Task List Page
    cy.url().should('include', '/applications/');
    Dashboardpage.extractReferenceNumber();
    OutgoingTrust.clickDetailsofTrustIfNotStarted();
    // Verify if the navigated to Outgoing Trust page
    cy.url().should('include', '/details-of-outgoing-trusts');
    // mark Complete Outgoing Trust details
     // Click on the Mark Complete checkbox
    Logger.log("Clicking the Mark Complete checkbox");
    OutgoingTrust.clickMarkCompleteCheckbox();
    // Click on the Save and Continue button
    Logger.log("Clicking the Save and Continue button on Details of Academies page");
    OutgoingTrust.clickSaveAndContinue();
    // Verify if the Task status is Completed
    OutgoingTrust.verifyTaskStatusIsCompleted();

    // Select Details of Academies task
    Logger.log("Clicking the Details of Academies task");
    Academies.clickDetailsOfAcademies();
    // Verify if the navigated to Details of Academies page
    cy.url().should('include', '/details-of-academies');
    //Click Mark as Complete
    Academies.clickMarkCompleteCheckbox();
    // Click on the Save and Continue button
    Logger.log("Clicking the Save and Continue button on Details of Academies page");
    Academies.clickSaveAndContinue();
    // Verify if the Task status is Completed
     Academies.verifyTaskStatusIsCompleted();
    //Select the Reasons and Benefits task
    Logger.log("Selecting the Reasons and Benefits task");
    ReasonsAndBenefits.clickReasonsIfNotStarted();
    ReasonsAndBenefits.TraversingRows();
    ReasonsAndBenefits.clickCompleteCheckbox();
   // Save and continue to complete the task
   cy.SaveTaskSummary();
   ReasonsAndBenefits.verifyTaskStatusIsCompleted();
    // Select Risks task
    Risks.clickRisksIfNotStarted();
    // Verify if the navigated to Risks page
    cy.url().should('include', '/risks');
    cy.clickMarkCompleteCheckbox();
    cy.SaveTaskSummary();
    Risks.verifyTaskStatusIsCompleted();
    //Incoming Trust Details
    IncomingTrust.clickDetailsofTrustIfNotStarted();
    // Verify if the navigated to Incoming Trust Details page
    cy.url().should('include', '/incoming-trust-details');
    cy.clickMarkCompleteCheckbox();
    cy.SaveTaskSummary();
    IncomingTrust.verifyTaskStatusIsCompleted();
    //Reasons and Benefits Incoming Trust Details
    ReasonsAndBenefitsIncoming.selectReasonsAndBenefits();
    // Verify if the navigated to Reasons and Benefits Incoming Trust Details page
    cy.url().should('include', 'reason-and-benefits-trust');
    cy.clickMarkCompleteCheckbox();
    cy.SaveTaskSummary();
    ReasonsAndBenefitsIncoming.verifyTaskStatusIsCompleted();
    //High Quality Inclusive Education
    HighQualityInclusiveEducation.clickHighQualityInclusiveEducationIfNotStarted();
    // Verify if the navigated to High Quality Inclusive Education page
    cy.url().should('include', '/high-quality-and-inclusive-education');
    cy.clickMarkCompleteCheckbox();
    cy.SaveTaskSummary();
    HighQualityInclusiveEducation.verifyTaskStatusIsCompleted();
    cy.wait(10000);
    //School Improvement
    SchoolImprovement.clickSchoolImprovementIfNotStarted();
    // Verify if the navigated to School Improvement page
    cy.url().should('include', '/school-improvement');
    cy.clickMarkCompleteCheckbox();
    cy.SaveTaskSummary();
    SchoolImprovement.verifyTaskStatusIsCompleted();
    cy.wait(10000);
    //Finance and Operations
    FinanceAndOperations.clickFinanceAndOperationsIfNotStarted();
    // Verify if the navigated to Finance and Operations page
    cy.url().should('include', '/finance-and-operations');
    cy.clickMarkCompleteCheckbox();
    cy.SaveTaskSummary();
    FinanceAndOperations.verifyTaskStatusIsCompleted();
    // Leadership
    Leadership.clickLeadershipIfNotStarted();
    //Verify if the navigated to Leadership page
    cy.url().should('include', '/leadership-and-work-force');
    cy.clickMarkCompleteCheckbox();
    cy.SaveTaskSummary();
    Leadership.verifyTaskStatusIsCompleted();
    //Members
    Members.clickMembersIfNotStarted();
    // Verify if the navigated to Members page
    cy.url().should('include', '/members');
    cy.clickMarkCompleteCheckbox();
    cy.SaveTaskSummary();
    Members.verifyTaskStatusIsCompleted();
    //Trustees
    Trustees.clickTrusteesIfNotStarted();
    // Verify if the navigated to Trustees page
    cy.url().should('include', '/trustees-task');
    cy.clickMarkCompleteCheckbox();
    cy.SaveTaskSummary();
    Trustees.verifyTaskStatusIsCompleted();
    //Governance Structure
    GovernanceStructure.clickGovernanceStructureIfNotStarted();
    // Verify if the navigated to Governance Structure page
    cy.url().should('include', '/governance-structure');
    cy.clickMarkCompleteCheckbox();
    cy.SaveTaskSummary();
    GovernanceStructure.verifyTaskStatusIsCompleted();
    //Declaration
    Declaration.clickDeclarationIfNotStarted();
    // Verify if the navigated to Declaration page
    cy.url().should('include', '/declaration-from-academy-trust-chair');
    cy.clickMarkCompleteCheckbox();
    cy.SaveTaskSummary();
    Declaration.verifyTaskStatusIsCompleted();
    // Verify if all the tasks are completed
   // Dashboardpage.verifyAllTasksAreCompleted();
   // Submit the application
   cy.getByClass('govuk-button').contains('Review application').click();
   cy.getById('submit-application-button').contains('Submit application').click();
   cy.getByClass('govuk-panel__title').should('have.text', 'Application submitted');
  //cy.getByClass('govuk-panel__body').contains('Your reference number'+ Cypress.env('referenceNumber'));




 });
   

  
    
 
});
