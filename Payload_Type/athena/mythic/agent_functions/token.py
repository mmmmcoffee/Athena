from mythic_payloadtype_container.MythicCommandBase import *
import json


class TokenArguments(TaskArguments):
    def __init__(self, command_line, **kwargs):
        super().__init__(command_line)
        self.args = [
            CommandParameter(
                name="action",
                cli_name="action",
                display_name="Action",
                description="The Action to perform with the plugin. [create, list, impersonate, revert]",
                type=ParameterType.String,
                default_value="list",
                parameter_group_info=[
                    ParameterGroupInfo(
                        required=True,
                        ui_position=0,
                        group_name="Default"  # Many Args
                    ),
                ],
            ),
            CommandParameter(
                name="domain",
                cli_name="domain",
                display_name="Domain",
                description="The domain to log on to (set to . for local accounts)",
                type=ParameterType.String,
                parameter_group_info=[
                    ParameterGroupInfo(
                        required=False,
                        group_name="Default"
                    )
                ],
            ),
            CommandParameter(
                name="username",
                cli_name="username",
                display_name="Username",
                description="The username to impersonate",
                type=ParameterType.String,
                default_value="",
                parameter_group_info=[
                    ParameterGroupInfo(
                        required=False,
                        group_name="Default"
                    )
                ],
            ),
            CommandParameter(
                name="password",
                cli_name="password",
                display_name="Password",
                description="The user password",
                type=ParameterType.String,
                default_value="",
                parameter_group_info=[
                    ParameterGroupInfo(
                        required=False,
                        group_name="Default"
                    )
                ],
            ),
            CommandParameter(
                name="netonly",
                cli_name="netonly",
                display_name="NetOnly",
                description="Perform a netonly logon",
                type=ParameterType.Boolean,
                default_value=False,
                parameter_group_info=[
                    ParameterGroupInfo(
                        required=False,
                        group_name="Default"
                    )
                ],
            ),
            CommandParameter(
                name="name",
                cli_name="name",
                display_name="Name",
                description="A descriptive name for the token",
                type=ParameterType.String,
                default_value="",
                parameter_group_info=[
                    ParameterGroupInfo(
                        required=False,
                        group_name="Default"
                    ),
                ],
            ),
        ]
    async def parse_arguments(self):
        pass


class TokenCommand(CommandBase):
    cmd = "token"
    needs_admin = False
    help_cmd = """
    Create a new token for a domain user:
    token make -username <user> -password <password> -domain <domain> -netonly true -name <descriptive name>
    token make -username myuser@contoso.com -password P@ssw0rd -netonly true
    token make -username myuser -password P@ssword -domain contoso.com -netonly false
    
    Create a new token for a local user:
    token make -username mylocaladmin -password P@ssw0rd! -domain . -netonly true
    
    Impersonate a created token    
    token impersonate -name <descriptive name>
    
    Stop impersonating, token can be re-used later
    token revert
    """
    description = "Change impersonation context for current user"
    version = 1
    is_exit = False
    is_file_browse = False
    is_process_list = False
    is_download_file = False
    is_upload_file = False
    is_remove_file = False
    supported_ui_features = []
    author = "@checkymander"
    argument_class = TokenArguments
    attackmapping = []
    attributes = CommandAttributes(
        builtin=True
    )

    async def create_tasking(self, task: MythicTask) -> MythicTask:
        return task

    async def process_response(self, response: AgentResponse):
        pass
